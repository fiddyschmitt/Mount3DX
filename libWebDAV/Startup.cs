using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using NWebDav.Server;
using NWebDav.Server.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using NWebDav.Server.Stores;
using Microsoft.AspNetCore.ResponseCompression;

namespace libWebDAV
{
    public class Startup
    {
#pragma warning disable CA2211 // Non-constant fields should not be visible
        public static IStore? Store;
#pragma warning restore CA2211 // Non-constant fields should not be visible

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging((logging) =>
            {
                //logging.AddDebug();
                //logging.AddConsole();
            });

            services.AddResponseCompression(options =>
            {
                options.Providers.Add<GzipCompressionProvider>();
                options.Providers.Add<BrotliCompressionProvider>();
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            // Create the request handler factory
            var requestHandlerFactory = new RequestHandlerFactory();

            app.UseResponseCompression();

            // Create WebDAV dispatcher
            var webDavDispatcher = new WebDavDispatcher(Store, requestHandlerFactory);

            app.Run(async context =>
            {
                // Create the proper HTTP context
                var httpContext = new AspNetCoreContext(context);

                // Dispatch request
                await webDavDispatcher.DispatchRequestAsync(httpContext).ConfigureAwait(false);
            });
        }
    }
}