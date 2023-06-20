using libCommon;
using libCommon.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Web;

namespace lib3dx
{
    public class _3dxServer
    {
        CancellationTokenSource CancellationTokenSource = new();
        Task? PingTask;

        public _3dxServer(string serverUrl, string cookies, bool keepAlive, int keepAliveIntervalMinutes)
        {
            ServerUrl = serverUrl;
            Cookies = cookies;
            KeepAlive = keepAlive;
            KeepAliveInterval = TimeSpan.FromMinutes(keepAliveIntervalMinutes);

            if (keepAlive)
            {
                PingTask = Task.Factory.StartNew(() =>
                {
                    while (!CancellationTokenSource.IsCancellationRequested)
                    {
                        Ping();
                        Task.Delay(KeepAliveInterval, CancellationTokenSource.Token).Wait();
                    }
                });
            }
        }

        public bool Ping()
        {
            var pingUrl = ServerUrl.UrlCombine(@"resources/v1/modeler/documents/ABC123AABEC11256");

            var success = false;

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", Cookies);
                var request = new HttpRequestMessage(HttpMethod.Get, pingUrl);

                try
                {
                    var response = httpClient.Send(request);
                    var responseStr = response.Content.ReadAsStringAsync().Result;
                    if (responseStr.Contains("Object Does Not Exist"))
                    {
                        success = true;
                    }
                }
                catch { }
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
        public string Cookies { get; }
        public bool KeepAlive { get; }
        public int QueryThreads { get; }
        public TimeSpan KeepAliveInterval { get; }
    }
}