using lib3dx;
using libCommon.Events;
using Mount3DX.Tests.Fake3dx;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Mount3DX.Tests
{
    public static class TestSessions
    {
        //Windows' default for the WebDAV client's FileAttributesLimitInBytes, which Form1 passes on
        public const uint WebClientListingLimit = 1_000_000;

        //The SMB share of the tests. Not 127.0.0.1, which a running Mount3DX uses: Windows ties a
        //port to a server name for every connection to it. A fixed port, because that tie outlives a
        //disconnect by a few seconds, so a run straight after another must use the same one.
        public static readonly IPAddress SmbAddress = IPAddress.Parse("127.0.0.2");
        public const int SmbPort = 1447;
        public const string ShareName = "3DX";
        public static string SmbUnc => $@"\\{SmbAddress}\{ShareName}";

        //A server for the fake 3DX, ready the way one is after a sign-in
        public static _3dxServer ServerFor(Fake3dxServer fake)
        {
            var server = new _3dxServer(fake.ServerUrl, serveMetadataFiles: true);
            Assert.IsTrue(server.FindServices(), "The fake 3DX didn't answer the pings.");
            return server;
        }

        //Starts a session against the fake 3DX as Form1 does once signed in. It never opens Explorer,
        //and its SMB cache is in a folder of its own.
        public static StartedSession Start(Fake3dxServer fake, Action<Settings> configure)
        {
            var settings = new Settings();
            settings._3dx.ServerUrl = fake.ServerUrl;
            settings._3dx.RefreshIntervalMinutes = 0;
            settings._3dx.KeepAliveIntervalMinutes = 0;
            settings._3dx.QueryThreads = 2;
            settings.Vfs.Smb.Address = SmbAddress.ToString();
            settings.Vfs.Smb.Port = SmbPort;
            settings.Vfs.Smb.ShareName = ShareName;
            settings.Vfs.Smb.CacheSizeMB = 256;
            configure(settings);

            var stateDirectory = Path.Combine(Path.GetTempPath(), "Mount3DX.Tests", $"state-{Guid.NewGuid():N}");
            var session = new Session(ServerFor(fake), settings, WebClientListingLimit, openInExplorer: false)
            {
                SmbStateDirectory = stateDirectory,
            };

            FinishedEventArgs? finished = null;
            session.InitialisationFinished += (_, e) => finished = e;
            session.Start();

            return new StartedSession(session, finished ?? throw new AssertFailedException("The session never said it had finished starting."), stateDirectory);
        }

        public static int FreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public static bool PortAnswers(IPAddress address, int port)
        {
            using var client = new TcpClient();
            try
            {
                return client.ConnectAsync(address, port).Wait(TimeSpan.FromSeconds(2)) && client.Connected;
            }
            catch (AggregateException)
            {
                return false;
            }
        }

        //The Windows WebDAV client is a service that starts when Explorer first opens a WebDAV
        //location (or with "sc start WebClient" as an administrator); file APIs don't start it
        public static bool WebClientRunning() => Run("sc.exe", "query WebClient").Contains("RUNNING", StringComparison.Ordinal);

        //The Windows SMB client's connections
        public static string NetUse() => Run("net.exe", "use");

        //The names in a folder, as programs see them. Through the Windows WebDAV client, .NET gets
        //every name with a NUL at its end (the client counts the terminator in the name's length;
        //Explorer and other Win32 programs read names as C strings and never notice), and so also
        //"." and "..", which it would otherwise leave out.
        public static IReadOnlyList<string> NamesIn(string folder) => [.. Directory
                                                                        .GetFileSystemEntries(folder)
                                                                        .Select(path => Path.GetFileName(path).TrimEnd('\0'))
                                                                        .Where(name => name is not ("." or ".."))];

        static string Run(string program, string arguments)
        {
            using var process = Process.Start(new ProcessStartInfo(program, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
    }

    public sealed record StartedSession(Session Session, FinishedEventArgs Finished, string StateDirectory);
}
