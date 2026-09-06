using System;

namespace Nafas.Observability
{
    /// <summary>
    /// Configuration for <c>services.AddNafasServer(options => { ... })</c> --
    /// mirrors Hangfire's <c>AddHangfireServer</c> options object shape.
    /// </summary>
    public class NafasServerOptions
    {
        /// <summary>
        /// Defaults to <see cref="DatabaseProvider.Sqlite"/> -- zero-config,
        /// nothing to install. Only change this if you actually need SQL Server.
        /// </summary>
        public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

        /// <summary>
        /// The connection string itself, set directly in code. For SQLite this
        /// is a normal SQLite connection string (e.g. "Data Source=nafas.db");
        /// if left null with <see cref="Provider"/> = Sqlite, a sensible default
        /// ("Data Source=nafas.db" in the app's content root) is used instead.
        /// For SqlServer, either this or <see cref="ConnectionStringName"/> is
        /// required -- there's no sensible SQL Server default to fall back to.
        ///
        /// Takes priority over <see cref="ConnectionStringName"/> when both are
        /// set.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Alternative to setting <see cref="ConnectionString"/> directly: the
        /// NAME of a connection string already defined by the consuming app, so
        /// this package doesn't need its own copy of a secret the app already
        /// manages. Resolved in this order:
        ///   1. <c>IConfiguration.GetConnectionString(name)</c> -- covers
        ///      appsettings.json's "ConnectionStrings" section (the standard
        ///      ASP.NET Core convention).
        ///   2. <c>System.Configuration.ConfigurationManager.ConnectionStrings
        ///      [name]</c> -- covers classic web.config's &lt;connectionStrings&gt;
        ///      section, for apps still on (or bridging from) .NET Framework /
        ///      System.Web, which never populates IConfiguration.
        /// See ConnectionStringResolver.cs for the actual resolution code.
        /// </summary>
        public string? ConnectionStringName { get; set; }

        /// <summary>
        /// SQL Server only. A named schema (e.g. "nafas") to create tables
        /// under, instead of the connection's default schema (usually "dbo") --
        /// keeps this package's tables clearly separated from the rest of the
        /// consuming app's own database if it shares one. Ignored for SQLite,
        /// which has no schema concept.
        /// </summary>
        public string? Schema { get; set; }

        /// <summary>
        /// How many days of logs/metrics/traces to keep before the retention
        /// sweep deletes them. Defaults to 30, matching the free tier's
        /// documented retention window (see ../CLAUDE.md).
        /// </summary>
        public int RetentionDays { get; set; } = 30;

        /// <summary>
        /// Where to POST an alert payload when a rule fires. This package
        /// doesn't send email/SMS/Slack itself -- it POSTs a JSON payload here
        /// and the consuming app's own system takes it from there (matches
        /// Hangfire.Pro's own pattern of staying unopinionated about delivery).
        /// Null means alert evaluation still runs and incidents still show up
        /// in the dashboard, just nothing gets POSTed anywhere.
        /// </summary>
        public string? AlertWebhookUrl { get; set; }

        /// <summary>
        /// The value written to every ingested log/metric/trace row's
        /// ServiceName column -- this app's own identity, in the OTel
        /// resource-attribute sense (<c>service.name</c>). Null (the
        /// default) resolves to <c>IHostEnvironment.ApplicationName</c> at
        /// startup (normally the entry assembly's name -- zero-config,
        /// nothing to set for a single-app deployment), falling back to
        /// <c>"unknown_service"</c> (OTel's own convention for a genuinely
        /// unresolvable name) if even that isn't available. Set explicitly
        /// when running multiple differently-named instances of the same
        /// app, or when ApplicationName isn't the name you want shown on the
        /// dashboard.
        /// </summary>
        public string? ServiceName { get; set; }
    }
}
