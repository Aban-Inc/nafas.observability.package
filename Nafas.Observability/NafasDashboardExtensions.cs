using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Nafas.Observability
{
    public static class NafasDashboardExtensions
    {
        // "/nafas", not "/" -- same reasoning as Hangfire's own default
        // ("/hangfire"): a mount point the consuming app almost certainly
        // isn't already using, while still being overridable per the
        // `path` parameter (see below) exactly like
        // app.UseHangfireDashboard("/some/other/path").
        private const string DefaultPath = "/nafas";

        /// <summary>
        /// Mounts the Nafas dashboard (the embedded Vue SPA under wwwroot/)
        /// at <paramref name="path"/>. The consuming app picks this path
        /// freely, same as Hangfire's UseHangfireDashboard(path) -- nothing
        /// here is hardcoded to "/nafas" beyond the default.
        /// </summary>
        public static IApplicationBuilder UseNafasDashboard(this IApplicationBuilder app, string path = DefaultPath)
        {
            if (app is null) throw new ArgumentNullException(nameof(app));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Dashboard path must not be empty.", nameof(path));

            var normalizedPath = "/" + path.Trim('/');
            var assembly = typeof(NafasDashboardExtensions).GetTypeInfo().Assembly;
            // The real, currently-installed package version -- not a
            // hardcoded display string (AppSidebar.vue used to show a
            // leftover "v3.8.1" from the SaaS platform screen this UI was
            // copied from, which was never this package's own version at
            // all). InformationalVersion honors a <Version>/<PackageVersion>
            // MSBuild property if one's ever set; falls back to the plain
            // assembly version (1.0.0.0 by default) when neither is. The
            // SDK appends "+<git-commit-hash>" to InformationalVersion by
            // default in a git repo -- real data, but a 40-character hash
            // has no business in this UI, so only the part before '+' is
            // used.
            var packageVersion = (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "0.0.0").Split('+')[0];
            // Matches MSBuild's default embedded-resource naming
            // (<RootNamespace>.<folder>.<file>) -- see the csproj's own
            // comment for why this isn't a custom LogicalName scheme.
            var fileProvider = new EmbeddedFileProvider(assembly, "Nafas.Observability.wwwroot");
            var contentTypeProvider = new FileExtensionContentTypeProvider();

            app.Map(normalizedPath, dashboardApp =>
            {
                // Serves every real embedded file (JS/CSS/SVG/font chunks)
                // at its own path under the mount point -- e.g.
                // {normalizedPath}/assets/index-XXXX.js.
                dashboardApp.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = fileProvider,
                    RequestPath = "",
                    ContentTypeProvider = contentTypeProvider,
                });

                // The dashboard's whole api/* surface (NafasDashboardEndpoints.cs's
                // own hand-rolled router, not ASP.NET Core's
                // IEndpointRouteBuilder -- see that file's own comment on
                // why). Falls through to next() (the SPA fallback below) for
                // anything that isn't a recognized api/* route, e.g. a
                // client-side route like {normalizedPath}/logs.
                dashboardApp.Use(async (context, next) =>
                {
                    if (!await NafasDashboardEndpoints.TryHandleAsync(context).ConfigureAwait(false))
                    {
                        await next().ConfigureAwait(false);
                    }
                });

                // SPA fallback: anything that reaches here (a client-side
                // route like {normalizedPath}/logs, not a real static file --
                // StaticFileMiddleware above already short-circuited real
                // asset requests) gets index.html, so vue-router's
                // createWebHistory can take over client-side. The injected
                // script sets window.__NAFAS_BASE_PATH__ so the SPA's own
                // router base and API calls resolve against wherever it's
                // actually mounted -- see ClientApp/src/utils/serviceBaseUrl.ts.
                dashboardApp.Run(async context =>
                {
                    var indexFile = fileProvider.GetFileInfo("index.html");
                    if (!indexFile.Exists)
                    {
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.WriteAsync("Nafas dashboard assets not found (wwwroot/index.html missing from the embedded resources).");
                        return;
                    }

                    string html;
                    using (var stream = indexFile.CreateReadStream())
                    using (var reader = new StreamReader(stream))
                    {
                        html = await reader.ReadToEndAsync();
                    }

                    // <base href> (trailing slash matters) fixes every
                    // relative URL in the page -- script/link src AND the
                    // SPA's own client-side fetch() calls -- regardless of
                    // whether the browser is at {normalizedPath} (no
                    // trailing slash) or a client-side route underneath it
                    // like {normalizedPath}/logs. Without it, the browser
                    // resolves "./assets/x.js" against whatever the current
                    // URL happens to be, e.g. mistakenly against the site
                    // root when there's no trailing slash on the request.
                    //
                    // MUST be the first thing after <head>, not appended
                    // before </head> -- a <base> tag only affects elements
                    // the parser reaches AFTER it; Vite's own injected
                    // <script src="./assets/...">/<link href="./assets/...">
                    // already sit earlier in this HTML, so appending at the
                    // end would silently do nothing for exactly the two
                    // tags that actually need it.
                    var injected = $"<base href=\"{normalizedPath}/\">"
                        + $"<script>window.__NAFAS_BASE_PATH__ = \"{normalizedPath}\"; window.__NAFAS_VERSION__ = \"{packageVersion}\";</script>";
                    html = html.Replace("<head>", "<head>" + injected);

                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync(html);
                });
            });

            return app;
        }
    }
}
