using libCommon;
using System.Net;
using System.Web;

namespace lib3dx
{
    public class _3dxServer
    {
        CancellationTokenSource CancellationTokenSource = new();
        Task? PingTask;

        public _3dxServer(string serverUrl, string cookiesString, bool keepAlive, int keepAliveIntervalMinutes)
        {
            ServerUrl = serverUrl;
            CookiesString = cookiesString;
            KeepAlive = keepAlive;
            KeepAliveInterval = TimeSpan.FromMinutes(keepAliveIntervalMinutes);

            if (keepAlive)
            {
                PingTask = Task.Factory.StartNew(() =>
                {
                    while (!CancellationTokenSource.IsCancellationRequested)
                    {
                        Task.Delay(KeepAliveInterval, CancellationTokenSource.Token);
                    }
                });
            }
        }

        public bool Ping()
        {
            var pingUrl = Path.Combine(ServerUrl, "res/v1/etc");

            var success = false;

            try
            {
                var pingResponse = Utility.DownloadString(pingUrl);

                if (pingResponse.Contains("blah"))
                {
                    success = true;
                }
            }
            catch
            { }

            return success;
        }

        public void Close()
        {
            CancellationTokenSource.Cancel();
            PingTask?.Wait();
        }

        public string ServerUrl { get; }
        public string CookiesString { get; }
        public bool KeepAlive { get; }
        public TimeSpan KeepAliveInterval { get; }
    }
}