using System;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Ingestion;
using Nafas.Observability.Storage;

namespace Nafas.Observability
{
    /// <summary>
    /// Wraps the resolved connection string so it's never registered as a bare
    /// <see cref="string"/> in the consuming app's DI container -- a raw
    /// string singleton would collide with (or accidentally satisfy) any other
    /// string dependency anywhere else in that container. Internal: nothing
    /// outside this package should ever need to ask for the resolved string
    /// directly, only through the services built from it.
    /// </summary>
    internal sealed class NafasConnectionString
    {
        public string Value { get; }
        public NafasConnectionString(string value) => Value = value;
    }

    /// <summary>
    /// Same reasoning as <see cref="NafasConnectionString"/>, applied to the
    /// dashboard's own cache: a bare <see cref="IMemoryCache"/> singleton
    /// would collide with (get shadowed by, or shadow) a consuming app's own
    /// <c>services.AddMemoryCache()</c> registration, since DI resolves the
    /// last registration for a given service type. Wrapping it means this
    /// package's cache is always its own dedicated instance regardless of
    /// what the app does with IMemoryCache elsewhere.
    /// </summary>
    internal sealed class NafasMemoryCache
    {
        public IMemoryCache Instance { get; }
        public NafasMemoryCache(IMemoryCache instance) => Instance = instance;
    }

    /// <summary>
    /// The resolved value of <see cref="NafasServerOptions.ServiceName"/> --
    /// wrapped for the same reason as <see cref="NafasConnectionString"/>
    /// (a bare <see cref="string"/> singleton would collide with any other
    /// string dependency in the consuming app's container). Consumed by
    /// every Ingestion/ listener that needs to stamp ServiceName onto a
    /// record.
    /// </summary>
    internal sealed class NafasServiceName
    {
        public string Value { get; }
        public NafasServiceName(string value) => Value = value;
    }

    public static class NafasServiceCollectionExtensions
    {
        /// <summary>
        /// Registers Nafas's storage (schema creation + retention sweep) and
        /// alert-webhook delivery. Mirrors Hangfire's
        /// <c>services.AddHangfireServer(options => { ... })</c> shape.
        /// Does NOT register the dashboard -- see NafasDashboardExtensions.cs's
        /// <c>app.UseNafasDashboard(path)</c> for that, called separately on
        /// the built <c>IApplicationBuilder</c>.
        /// </summary>
        public static IServiceCollection AddNafasServer(this IServiceCollection services, Action<NafasServerOptions>? configure = null)
        {
            if (services is null) throw new ArgumentNullException(nameof(services));

            var options = new NafasServerOptions();
            configure?.Invoke(options);

            // IConfiguration is normally already registered by this point --
            // WebApplicationBuilder and Host.CreateDefaultBuilder both add it
            // long before user code gets a chance to call AddNafasServer.
            // Building a temporary provider just to read it back out, once,
            // at startup, is the standard way an IServiceCollection extension
            // method reaches configuration before the real provider exists;
            // see ConnectionStringResolver.cs for what it's used for.
            var tempProvider = services.BuildServiceProvider();
            var configuration = tempProvider.GetService<IConfiguration>();
            var connectionString = ConnectionStringResolver.Resolve(options, configuration);

            // Same temporary-provider trick as IConfiguration above, for the
            // same reason -- IHostEnvironment is normally already registered
            // by this point (WebApplicationBuilder/Host.CreateDefaultBuilder),
            // and this is the standard way an IServiceCollection extension
            // reaches it before the real provider exists.
            var serviceName = options.ServiceName
                ?? tempProvider.GetService<IHostEnvironment>()?.ApplicationName
                ?? "unknown_service"; // OTel's own convention for a genuinely unresolvable service.name

            services.AddSingleton(options);
            services.AddSingleton(new NafasConnectionString(connectionString));
            services.AddSingleton(new NafasServiceName(serviceName));

            services.AddSingleton<INafasSchemaProvisioner>(_ =>
                options.Provider == DatabaseProvider.Sqlite
                    ? (INafasSchemaProvisioner)new SqliteSchemaProvisioner(connectionString)
                    : new SqlServerSchemaProvisioner(connectionString, options.Schema));

            services.AddSingleton<INafasQueryStore>(_ =>
                options.Provider == DatabaseProvider.Sqlite
                    ? (INafasQueryStore)new SqliteQueryStore(connectionString)
                    : new SqlServerQueryStore(connectionString, options.Schema));

            services.AddSingleton<INafasIngestionWriter>(_ =>
                options.Provider == DatabaseProvider.Sqlite
                    ? (INafasIngestionWriter)new SqliteIngestionWriter(connectionString)
                    : new SqlServerIngestionWriter(connectionString, options.Schema));

            // In-process cache for the dashboard's own read endpoints -- see
            // this csproj's own comment on Caching.Memory for why this
            // instead of Redis, and NafasMemoryCache's own comment for why
            // it's wrapped instead of registered as a bare IMemoryCache.
            services.AddSingleton(new NafasMemoryCache(new MemoryCache(new MemoryCacheOptions())));

            // Backs the dashboard's SSE streams -- see NafasLiveFeed.cs's own
            // comment. A distinctly-named type, unlike NafasConnectionString/
            // NafasMemoryCache's reasoning, so no collision risk registering
            // it directly.
            services.AddSingleton<NafasLiveFeed>();

            // Named/typed HttpClient for NafasAlertWebhookSender -- gets
            // HttpClientFactory's normal pooling/lifetime handling instead of
            // this package new-ing up its own HttpClient.
            services.AddHttpClient<INafasAlertWebhookSender, NafasAlertWebhookSender>();

            // ---- Real ingestion (Ingestion/) -- turns "the dashboard
            // exists" into "the dashboard shows this app's own real
            // logs/metrics/traces", with no code changes required in the
            // consuming app beyond this one call. See NafasIngestionQueue's
            // own comment for why every listener below only ever enqueues,
            // never writes to the database directly.
            services.AddSingleton<NafasIngestionQueue>();
            // Registered as ILoggerProvider (not a concrete type) so ASP.NET
            // Core's own ILoggerFactory picks it up automatically -- every
            // ILogger<T> call anywhere in the app or its dependencies starts
            // flowing here the moment AddNafasServer() runs, same as adding
            // any other logging sink (Serilog, NLog, ...).
            services.AddSingleton<ILoggerProvider, NafasLoggerProvider>();
            services.AddHostedService<NafasActivityIngestionHostedService>();
            services.AddHostedService<NafasMeterIngestionHostedService>();
            services.AddHostedService<NafasResourceMetricsHostedService>();
            services.AddHostedService<NafasIngestionWriterHostedService>();

            services.AddHostedService<NafasSchemaInitializationHostedService>();
            services.AddHostedService<NafasRetentionHostedService>();
            // Evaluates nafas_alert_rules against real ingested data and
            // opens/resolves nafas_alert_incidents -- see its own comment.
            // Depends on INafasQueryStore/NafasLiveFeed/
            // INafasAlertWebhookSender, all already registered above.
            services.AddHostedService<NafasAlertEvaluationHostedService>();

            return services;
        }
    }
}
