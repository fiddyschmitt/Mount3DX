using Microsoft.AspNetCore.Hosting;

using NWebDav.Server.Logging;

using Microsoft.Extensions.Hosting;
using NWebDav.Server;
using NWebDav.Sample.Kestrel;
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
            runningHost?.StopAsync();
        }
    }
}