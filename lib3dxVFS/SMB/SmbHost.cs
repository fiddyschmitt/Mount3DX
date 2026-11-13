using libCommon;
using libCommon.Vfs;
using LibSmb;
using Microsoft.Extensions.Logging;
using System.Net;

namespace lib3dxVFS.SMB
{
    //Serves a tree (reads only) as an SMB share on this machine: LibSmb's server, in this process,
    //reading the tree through VfsFileSystem, and the Windows client's connection to the share. Start
    //proves the whole chain with an empty tree first, so a Windows that can't do this is known
    //before the documents are fetched; the tree is handed over through Source once it is loaded.
    public sealed class SmbHost : IDisposable
    {
        //The share has a fixed login: the server only listens on this machine, and the tree is
        //whatever the signed-in user can already see in 3DX
        public const string UserName = "mount3dx";
        public const string Password = "mount3dx";

        public const long DefaultCacheSizeBytes = 2L << 30;

        public static string DefaultStateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mount3DX");

        readonly SmbServer server;
        readonly VfsFileSystem fileSystem;
        readonly ChunkCache cache;
        bool disposed;

        public string UncPath { get; }

        //Raised when the server stops accepting connections on its own, with the reason
        public event Action<string>? ServerDied;

        SmbHost(string uncPath, SmbServer server, VfsFileSystem fileSystem, ChunkCache cache)
        {
            UncPath = uncPath;
            this.server = server;
            this.fileSystem = fileSystem;
            this.cache = cache;

            server.Faulted += ex => ServerDied?.Invoke($"The SMB server stopped accepting connections: {ex.Message}");
        }

        public IVfsSource? Source
        {
            get => fileSystem.Source;
            set => fileSystem.Source = value;
        }

        //Everything that could go wrong is checked here and nothing is left running on failure
        public static SmbHost Start(IPAddress address, int port, string shareName, string? stateDirectory = null, long cacheSizeBytes = DefaultCacheSizeBytes)
        {
            if (!WindowsSmbClient.IsSupported)
            {
                throw new InvalidOperationException("This Windows can't connect to an SMB server on a port other than 445 (Windows 11 24H2 or later is needed).");
            }

            stateDirectory ??= DefaultStateDirectory;
            var unc = $@"\\{address}\{shareName}";

            //a connection left by an earlier run (a crash) would keep its old port
            WindowsSmbClient.Disconnect(unc);

            var cache = new ChunkCache(Path.Combine(stateDirectory, "cache"), cacheSizeBytes);
            var fileSystem = new VfsFileSystem(cache);
            SmbServer? server = null;

            try
            {
                //A read-only share: the server refuses a write, a delete or a rename up front ("the
                //media is write protected") rather than at the close of the handle, where Windows
                //ignores the answer and a delete would look successful
                server = SmbServer.Start(
                    new SmbServerOptions
                    {
                        EndPoint = new IPEndPoint(address, port),
                        User = UserName,
                        Password = Password,
                        Logger = new LogFileLogger(),
                    },
                    [new SmbShare(shareName, fileSystem, readOnly: true)]);

                Log.WriteLine($"SMB server (LibSmb {typeof(SmbServer).Assembly.GetName().Version?.ToString(3)}) listening on {server.LocalEndPoint}.");

                WindowsSmbClient.Connect(unc, port, UserName, Password);

                //the client, the server and the file system, end to end (the root is empty for now)
                Directory.GetFileSystemEntries(unc);
                Log.WriteLine($"SMB share {unc} is reachable.");

                return new SmbHost(unc, server, fileSystem, cache);
            }
            catch
            {
                WindowsSmbClient.Disconnect(unc);
                if (server != null)
                {
                    Stop(server);
                }
                cache.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            //the client first, so it drops the connection cleanly rather than discovering a dead server
            WindowsSmbClient.Disconnect(UncPath);
            Stop(server);
            cache.Dispose();
        }

        //On the thread pool, so a caller with a SynchronizationContext (the UI) can't deadlock it
        static void Stop(SmbServer server)
        {
            try
            {
                Task.Run(async () => await server.DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
                Log.WriteLine("SMB server stopped.");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while stopping the SMB server: {ex.Message}");
            }
        }

        //LibSmb's connections, sign-ins and failures go to the log file; its trace of every request
        //does not
        sealed class LogFileLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Debug and < LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                //an unexpected failure gets its stack; an expected one just its message
                var detail = exception == null ? ""
                                : logLevel >= LogLevel.Warning ? $"{Environment.NewLine}{exception}"
                                : $" ({exception.Message})";

                libCommon.Log.WriteLine($"{formatter(state, exception)}{detail}");
            }
        }
    }
}
