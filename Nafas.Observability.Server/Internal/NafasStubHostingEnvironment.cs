using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Nafas.Observability.Server.Internal
{
    /// <summary>
    /// A minimal <see cref="IHostingEnvironment"/> for
    /// <c>Microsoft.AspNetCore.StaticFiles</c>' <c>StaticFileMiddleware</c>,
    /// which -- being real ASP.NET Core middleware, activated via
    /// <c>ActivatorUtilities</c> -- requires one in its constructor
    /// regardless of who's hosting it. A plain WinForms/WPF/console app's
    /// own <see cref="System.IServiceProvider"/> (what
    /// <c>NafasDashboardExtensions.UseNafasDashboard</c>'s dashboard
    /// dependencies -- <c>NafasServerOptions</c>, <c>INafasQueryStore</c>,
    /// etc. -- are correctly resolved from) has no reason to register an
    /// ASP.NET Core-specific hosting-environment service, so
    /// <see cref="NafasHttpServer"/> supplies this stub itself, as a
    /// fallback used only for the raw listener's own otherwise-empty
    /// <c>ApplicationBuilder.ApplicationServices</c> -- see
    /// NafasFallbackServiceProvider.cs.
    ///
    /// <c>NafasDashboardExtensions.cs</c> always sets
    /// <c>StaticFileOptions.FileProvider</c> explicitly (the embedded
    /// wwwroot resources), so this stub's own
    /// <see cref="WebRootFileProvider"/>/<see cref="ContentRootFileProvider"/>
    /// are never actually read by <c>StaticFileMiddleware</c> in practice --
    /// <see cref="NullFileProvider"/> only guards against the
    /// (currently unused) possibility of something dereferencing them
    /// unconditionally.
    /// </summary>
    internal sealed class NafasStubHostingEnvironment : IHostingEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Nafas.Observability.Server";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
