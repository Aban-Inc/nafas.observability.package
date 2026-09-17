using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Nafas.Observability.Server
{
    public static class NafasServerServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="NafasHttpServer"/> -- a standalone HTTP
        /// listener that serves the Nafas dashboard on its own port, for
        /// apps (WinForms, WPF, console/worker services) with no ASP.NET
        /// Core pipeline of their own to mount <c>app.UseNafasDashboard</c>
        /// on.
        ///
        /// Requires <c>services.AddNafasServer(...)</c> to already be called
        /// -- this only adds the listener, it doesn't set up storage,
        /// ingestion, or any of the dashboard's actual data (same division
        /// of responsibility as <c>UseNafasDashboard</c> in the embedded
        /// case). Enforced at startup, not here, so call order between this
        /// and <c>AddNafasServer</c> doesn't matter.
        /// </summary>
        public static IServiceCollection AddNafasHttpServer(this IServiceCollection services, Action<NafasHttpServerOptions>? configure = null)
        {
            if (services is null) throw new ArgumentNullException(nameof(services));

            var options = new NafasHttpServerOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<NafasHttpServer>();
            // Exposed as IHostedService too so NafasHttpServerOptions.Enabled
            // (if true) starts it automatically with the rest of the host's
            // hosted services -- resolves the SAME singleton instance
            // registered above, not a second one, so callers that also want
            // to start/stop it on demand (see NafasHttpServer's own comment)
            // are always talking to the one that's actually running.
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<NafasHttpServer>());

            return services;
        }
    }
}
