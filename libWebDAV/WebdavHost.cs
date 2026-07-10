using Microsoft.AspNetCore.Hosting;

using NWebDav.Server.Logging;

using Microsoft.Extensions.Hosting;
using NWebDav.Server;
using LogLevel = NWebDav.Server.Logging.LogLevel;
using LoggerFactory = NWebDav.Server.Logging.LoggerFactory;
using NWebDav.Server.Stores;

namespace libWebDAV
{
    public class WebdavHost
    {
        IHost? runningHost;

        public WebdavHost(string hostUrls, IStore store)
        {
            HostUrls = hostUrls;
            Startup.Store = store;
        }

        public string HostUrls { get; }

        public void Start()
        {
            var args = new[] { "--urls", HostUrls };

            // Use debug output for logging
            var adapter = new DebugOutputAdapter();
            //adapter.LogLevels.Add(LogLevel.Debug);
            //adapter.LogLevels.Add(LogLevel.Info);
            adapter.LogLevels.Add(LogLevel.Error);

            LoggerFactory.Factory = adapter;

            runningHost = Host
                        .CreateDefaultBuilder(args)
                        //.UseUrls("http://*:11000")
                        .ConfigureWebHostDefaults(webBuilder =>
                        {
                            /*
                            webBuilder.ConfigureKestrel(serverOptions =>
                            {
                                serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(10);
                            });
                            */

                            webBuilder.UseStartup<Startup>();
                        })
                        .Build();

            runningHost.Run();
        }

        public void Stop()
        {
            var host = runningHost;
            runningHost = null;
            if (host == null) return;

            try
            {
                //wait for shutdown so the port is released before a potential restart.
                //Run() disposes the host when it returns.
                host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (ObjectDisposedException)
            {
                //the host had already stopped and been disposed by Run()
            }
        }
    }
}