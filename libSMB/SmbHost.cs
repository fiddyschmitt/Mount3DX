using libCommon;
using libCommon.Vfs;
using System.Net;

namespace libSMB
{
    //Serves a tree (reads only) as an SMB share on this machine: the VFS endpoint, the bundled
    //portable-smb-server reading from it, and the Windows client's connection to the share. Start
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

        readonly VfsEndpoint endpoint;
        readonly SmbServerProcess server;
        readonly ChunkCache cache;
        bool disposed;

        public string UncPath { get; }

        //Raised when the server exits on its own, with the reason
        public event Action<string>? ServerDied;

        SmbHost(string uncPath, VfsEndpoint endpoint, SmbServerProcess server, ChunkCache cache)
        {
            UncPath = uncPath;
            this.endpoint = endpoint;
            this.server = server;
            this.cache = cache;

            server.Died += message => ServerDied?.Invoke(message);
        }

        public IVfsSource? Source
        {
            get => endpoint.Source;
            set => endpoint.Source = value;
        }

        //Everything that could go wrong is checked here and nothing is left running on failure
        public static SmbHost Start(IPAddress address, int port, string shareName, string? stateDirectory = null, long cacheSizeBytes = DefaultCacheSizeBytes)
        {
            if (!SmbConnection.IsSupported)
            {
                throw new InvalidOperationException("This Windows can't connect to an SMB server on a port other than 445 (Windows 11 24H2 or later is needed).");
            }

            stateDirectory ??= DefaultStateDirectory;
            var unc = $@"\\{address}\{shareName}";

            //a connection left by an earlier run (a crash) would keep its old port
            SmbConnection.Disconnect(unc);

            var cache = new ChunkCache(Path.Combine(stateDirectory, "cache"), cacheSizeBytes);
            VfsEndpoint? endpoint = null;
            SmbServerProcess? server = null;

            try
            {
                endpoint = VfsEndpoint.Start(shareName, cache);

                var binary = SmbServerBinary.Locate(stateDirectory);
                server = SmbServerProcess.Start(binary, address, port, UserName, Password, endpoint.BaseUrl, shareName, endpoint.Token);

                SmbConnection.Connect(unc, port, UserName, Password);

                //the client, the server and the endpoint, end to end (the root is empty for now)
                Directory.GetFileSystemEntries(unc);
                Log.WriteLine($"SMB share {unc} is reachable.");

                return new SmbHost(unc, endpoint, server, cache);
            }
            catch
            {
                SmbConnection.Disconnect(unc);
                server?.Dispose();
                endpoint?.Dispose();
                cache.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            //the client first, so it drops the connection cleanly rather than discovering a dead server
            SmbConnection.Disconnect(UncPath);
            server.Dispose();
            endpoint.Dispose();
            cache.Dispose();
        }
    }
}
