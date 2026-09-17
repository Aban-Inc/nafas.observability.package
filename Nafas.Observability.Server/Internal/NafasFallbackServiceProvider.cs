using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nafas.Observability.Server.Internal
{
    /// <summary>
    /// The <see cref="IServiceProvider"/> behind the raw listener's own
    /// (otherwise-empty) <c>ApplicationBuilder.ApplicationServices</c>.
    ///
    /// Real ASP.NET Core middleware activated via <c>ActivatorUtilities</c>
    /// -- concretely, <c>Microsoft.AspNetCore.StaticFiles</c>'s
    /// <c>StaticFileMiddleware</c>, the one piece of actual framework
    /// middleware <c>UseNafasDashboard</c> mounts -- resolves its OTHER
    /// constructor dependencies (<c>IHostingEnvironment</c>,
    /// <c>ILoggerFactory</c>) from whatever <c>IApplicationBuilder</c> it
    /// was built on, regardless of who's hosting it. A plain WinForms/WPF
    /// console app's own <see cref="IServiceProvider"/> was never going to
    /// have an ASP.NET Core-specific <c>IHostingEnvironment</c> registered
    /// (there's no ASP.NET Core hosting happening in that process at all,
    /// only Nafas's own services from <c>AddNafasServer()</c>) -- this type
    /// checks the real host provider FIRST for everything (so if it DOES
    /// have its own <c>ILoggerFactory</c>, as any app built on the .NET
    /// Generic Host normally does, that's the one used, and Nafas's own
    /// logs go wherever that app already sends them) and only falls back to
    /// a minimal stub for the handful of ASP.NET Core-specific services nothing
    /// outside an actual web host would ever register.
    /// </summary>
    internal sealed class NafasFallbackServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _hostServices;
        private readonly IServiceProvider _fallbackServices;

        public NafasFallbackServiceProvider(IServiceProvider hostServices)
        {
            _hostServices = hostServices;

            var fallback = new ServiceCollection();
            fallback.AddSingleton<IHostingEnvironment>(new NafasStubHostingEnvironment());
            // Only reached if the host app's own container genuinely has no
            // ILoggerFactory of its own (e.g. a bare ServiceCollection built
            // by hand, with no Generic Host and no AddLogging() call) --
            // NullLoggerFactory is the real, allocation-free "discard
            // everything" implementation from
            // Microsoft.Extensions.Logging.Abstractions, not a hand-rolled
            // stand-in.
            fallback.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            _fallbackServices = fallback.BuildServiceProvider();
        }

        public object? GetService(Type serviceType) =>
            _hostServices.GetService(serviceType) ?? _fallbackServices.GetService(serviceType);
    }
}
