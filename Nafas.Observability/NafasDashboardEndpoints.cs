using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Nafas.Observability.Storage;

namespace Nafas.Observability
{
    /// <summary>
    /// The dashboard's entire "api/*" surface -- a small hand-rolled router,
    /// not ASP.NET Core's IEndpointRouteBuilder/MapGet. That's deliberate:
    /// IEndpointRouteBuilder has had no standalone NuGet package since
    /// Endpoint Routing shipped in ASP.NET Core 3.0 -- it's
    /// FrameworkReference-only from there on, and FrameworkReference isn't
    /// available to a netstandard2.x class library at all (confirmed the
    /// hard way: Microsoft.AspNetCore.Routing 2.2.2, the last version with a
    /// real package, predates IEndpointRouteBuilder entirely). Hangfire hits
    /// the exact same constraint and solves it the same way -- its own
    /// dashboard has always used a small hand-rolled route table, not
    /// ASP.NET Core's routing system, for this reason (also covers its own
    /// OWIN hosting story, moot here, but the netstandard-compatibility
    /// reasoning is identical).
    ///
    /// Caching: KPI endpoints cache 30s, chart endpoints 60s -- the same
    /// convention the SaaS platform repo's Redis cache used (see
    /// NafasServiceCollectionExtensions.cs's own comment on why this is
    /// IMemoryCache instead of Redis here), per the explicit "dashboard data
    /// should hit the database as little as possible" requirement. Search/
    /// list/stream endpoints are never cached -- same "Recent Logs: NO
    /// cache" rule the SaaS platform repo documents, generalized to every
    /// live/on-demand query here, not just the log stream.
    /// </summary>
    internal static class NafasDashboardEndpoints
    {
        private static readonly TimeSpan KpiTtl = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ChartTtl = TimeSpan.FromSeconds(60);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private sealed class Route
        {
            public string Method = "GET";
            public string[] Segments = Array.Empty<string>();
            public Func<HttpContext, IReadOnlyDictionary<string, string>, Task> Handler = null!;
        }

        private static readonly List<Route> Routes = Build();

        /// <summary>
        /// Tries to match and handle <paramref name="context"/>'s request
        /// against the route table. Returns false (handles nothing) for any
        /// request that isn't a recognized api/* route, so the caller
        /// (NafasDashboardExtensions.cs) can fall through to static
        /// files/the SPA fallback exactly as if this middleware weren't
        /// there.
        /// </summary>
        public static async Task<bool> TryHandleAsync(HttpContext context)
        {
            var requestSegments = context.Request.Path.Value!.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var route in Routes)
            {
                if (!string.Equals(route.Method, context.Request.Method, StringComparison.OrdinalIgnoreCase)) continue;
                if (route.Segments.Length != requestSegments.Length) continue;

                var routeValues = new Dictionary<string, string>();
                var matched = true;
                for (var i = 0; i < route.Segments.Length; i++)
                {
                    var template = route.Segments[i];
                    if (template.StartsWith("{", StringComparison.Ordinal) && template.EndsWith("}", StringComparison.Ordinal))
                    {
                        routeValues[template.Substring(1, template.Length - 2)] = Uri.UnescapeDataString(requestSegments[i]);
                    }
                    else if (!string.Equals(template, requestSegments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        matched = false;
                        break;
                    }
                }

                if (!matched) continue;

                await route.Handler(context, routeValues).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        private static List<Route> Build()
        {
            var routes = new List<Route>();
            void Get(string pattern, Func<HttpContext, IReadOnlyDictionary<string, string>, Task> handler) => routes.Add(new Route { Method = "GET", Segments = Split(pattern), Handler = handler });
            void Post(string pattern, Func<HttpContext, IReadOnlyDictionary<string, string>, Task> handler) => routes.Add(new Route { Method = "POST", Segments = Split(pattern), Handler = handler });
            void Put(string pattern, Func<HttpContext, IReadOnlyDictionary<string, string>, Task> handler) => routes.Add(new Route { Method = "PUT", Segments = Split(pattern), Handler = handler });
            void Delete(string pattern, Func<HttpContext, IReadOnlyDictionary<string, string>, Task> handler) => routes.Add(new Route { Method = "DELETE", Segments = Split(pattern), Handler = handler });

            // ---- Logs ----
            Get("api/logs/kpi/error-rate/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetErrorRateKpiAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/logs/kpi/log-volume/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetLogVolumeKpiAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/logs/charts/volume-over-time/{from}/{to}/{interval}", Cached(ChartTtl, (ctx, rv, store) =>
                store.GetVolumeOverTimeAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), RouteInt(rv, "interval", 30), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/logs/kpi/error-rate/by-service/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetErrorRateByServiceAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), ctx.RequestAborted)));
            Get("api/logs/search/{from}/{to}", Uncached((ctx, rv, store) =>
                store.SearchLogsAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "level"), Query(ctx, "service"), Query(ctx, "search"), QueryInt(ctx, "page", 1), QueryInt(ctx, "pageSize", 50), ctx.RequestAborted)));
            Get("api/logs/stream", (ctx, _) => Sse(ctx, "logs"));

            // ---- Metrics ----
            Get("api/metrics/kpi/endpoints/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetMetricEndpointsKpiAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/metrics/kpi/overview/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetMetricsOverviewKpiAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/metrics/charts/cpu-usage/{from}/{to}/{interval}", Cached(ChartTtl, (ctx, rv, store) =>
                store.GetMetricTrendAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), RouteInt(rv, "interval", 30), "cpu_usage", Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/metrics/charts/memory-usage/{from}/{to}/{interval}", Cached(ChartTtl, (ctx, rv, store) =>
                store.GetMetricTrendAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), RouteInt(rv, "interval", 30), "memory_usage", Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/metrics/kpi/service-overview/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetServiceOverviewAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), ctx.RequestAborted)));

            // ---- Traces ----
            Get("api/traces/kpi/active/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetActiveTracesKpiAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/traces/kpi/overview/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.GetTracesOverviewKpiAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/traces/charts/latency-heatmap/{from}/{to}", Cached(ChartTtl, (ctx, rv, store) =>
                store.GetLatencyHeatmapAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), QueryInt(ctx, "bucket_hours", 1), Query(ctx, "service_name"), ctx.RequestAborted)));
            Get("api/traces/search/{from}/{to}", Uncached((ctx, rv, store) =>
                store.SearchTracesAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), Query(ctx, "service"), Query(ctx, "traceId"), QueryInt(ctx, "page", 1), QueryInt(ctx, "pageSize", 50), ctx.RequestAborted)));

            // ---- Service catalog ----
            Get("api/services/list/{from}/{to}", Cached(KpiTtl, (ctx, rv, store) =>
                store.ListServicesAsync(RouteDate(rv, "from"), RouteDate(rv, "to"), ctx.RequestAborted)));

            // ---- Alerts (rule/incident CRUD -- writes, never cached) ----
            Get("api/v1/rules", Uncached((ctx, _, store) => store.ListAlertRulesAsync(ctx.RequestAborted)));
            Post("api/v1/rules", async (context, _) =>
            {
                var store = context.RequestServices.GetRequiredService<INafasQueryStore>();
                var request = await JsonSerializer.DeserializeAsync<CreateAlertRuleRequest>(context.Request.Body, JsonOptions, context.RequestAborted).ConfigureAwait(false);
                var created = await store.CreateAlertRuleAsync(request!, context.RequestAborted).ConfigureAwait(false);
                await WriteEnvelopeAsync(context, created).ConfigureAwait(false);
            });
            Put("api/v1/rules/{id}", async (context, rv) =>
            {
                var store = context.RequestServices.GetRequiredService<INafasQueryStore>();
                var request = await JsonSerializer.DeserializeAsync<UpdateAlertRuleRequest>(context.Request.Body, JsonOptions, context.RequestAborted).ConfigureAwait(false);
                var updated = await store.UpdateAlertRuleAsync(RouteLong(rv, "id"), request!, context.RequestAborted).ConfigureAwait(false);
                await WriteEnvelopeAsync(context, updated).ConfigureAwait(false);
            });
            Delete("api/v1/rules/{id}", async (context, rv) =>
            {
                var store = context.RequestServices.GetRequiredService<INafasQueryStore>();
                await store.DeleteAlertRuleAsync(RouteLong(rv, "id"), context.RequestAborted).ConfigureAwait(false);
                await WriteEnvelopeAsync<object?>(context, null).ConfigureAwait(false);
            });
            Get("api/v1/incidents", Uncached((ctx, _, store) => store.ListAlertIncidentsAsync(ctx.RequestAborted)));
            Get("api/alerts/stream", (ctx, _) => Sse(ctx, "alerts"));

            // No api/v1/channels route -- alert delivery is one developer-
            // configured webhook (NafasServerOptions.AlertWebhookUrl, see
            // NafasAlertWebhookSender.cs), not a runtime-managed
            // per-installation channel list. This used to be stubbed here
            // (an empty array) for a Channels UI section that's since been
            // removed from alerts.vue -- see that file's own comment.

            return routes;
        }

        private static string[] Split(string pattern) => pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // ---- handler factories ----

        private static Func<HttpContext, IReadOnlyDictionary<string, string>, Task> Cached<T>(TimeSpan ttl, Func<HttpContext, IReadOnlyDictionary<string, string>, INafasQueryStore, Task<T>> query) => async (context, routeValues) =>
        {
            var cache = context.RequestServices.GetRequiredService<NafasMemoryCache>();
            var store = context.RequestServices.GetRequiredService<INafasQueryStore>();
            var cacheKey = context.Request.Path + context.Request.QueryString;

            if (!cache.Instance.TryGetValue(cacheKey, out T content))
            {
                content = await query(context, routeValues, store).ConfigureAwait(false);
                cache.Instance.Set(cacheKey, content, ttl);
            }

            await WriteEnvelopeAsync(context, content).ConfigureAwait(false);
        };

        private static Func<HttpContext, IReadOnlyDictionary<string, string>, Task> Uncached<T>(Func<HttpContext, IReadOnlyDictionary<string, string>, INafasQueryStore, Task<T>> query) => async (context, routeValues) =>
        {
            var store = context.RequestServices.GetRequiredService<INafasQueryStore>();
            var content = await query(context, routeValues, store).ConfigureAwait(false);
            await WriteEnvelopeAsync(context, content).ConfigureAwait(false);
        };

        // Subscribes to channelName on NafasLiveFeed and relays every
        // published message straight through as an SSE `data:` frame --
        // never touches the database (see NafasLiveFeed.cs's own comment on
        // why). Sends a `: ping` comment instead whenever 15s pass with
        // nothing published, both to keep the connection alive through
        // intermediary timeouts and so the frontend's EventSource still
        // looks "connected" during genuinely quiet periods.
        private static async Task Sse(HttpContext context, string channelName)
        {
            context.Response.Headers["Content-Type"] = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var feed = context.RequestServices.GetRequiredService<NafasLiveFeed>();
            var (id, reader) = feed.Subscribe(channelName);

            try
            {
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                    idleCts.CancelAfter(TimeSpan.FromSeconds(15));

                    string frame;
                    try
                    {
                        var message = await reader.ReadAsync(idleCts.Token).ConfigureAwait(false);
                        frame = $"data: {message}\n\n";
                    }
                    catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
                    {
                        // The 15s idle timeout fired, not a real disconnect.
                        frame = ": ping\n\n";
                    }

                    await context.Response.WriteAsync(frame, context.RequestAborted).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected -- expected, not an error.
            }
            finally
            {
                feed.Unsubscribe(channelName, id);
            }
        }

        private static async Task WriteEnvelopeAsync<T>(HttpContext context, T content)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            var envelope = new { content, message = "OK", success = true, errors = new Dictionary<string, string>() };
            await JsonSerializer.SerializeAsync(context.Response.Body, envelope, JsonOptions, context.RequestAborted).ConfigureAwait(false);
        }

        // ---- route/query parsing ----

        private static DateTime RouteDate(IReadOnlyDictionary<string, string> routeValues, string name) =>
            DateTime.Parse(routeValues[name], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        private static long RouteLong(IReadOnlyDictionary<string, string> routeValues, string name) => long.Parse(routeValues[name], CultureInfo.InvariantCulture);

        private static int RouteInt(IReadOnlyDictionary<string, string> routeValues, string name, int fallback) =>
            routeValues.TryGetValue(name, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

        private static string? Query(HttpContext context, string name) => context.Request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

        private static int QueryInt(HttpContext context, string name, int fallback) => int.TryParse(Query(context, name), out var value) ? value : fallback;
    }
}
