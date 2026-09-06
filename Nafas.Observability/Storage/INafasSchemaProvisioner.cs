using System.Threading;
using System.Threading.Tasks;

namespace Nafas.Observability.Storage
{
    /// <summary>
    /// Creates this package's own tables if they don't already exist. One
    /// implementation per <see cref="DatabaseProvider"/> (SqliteSchemaProvisioner,
    /// SqlServerSchemaProvisioner) -- genuinely polymorphic (selected at runtime
    /// from <see cref="NafasServerOptions.Provider"/>), unlike the SaaS platform
    /// repo's services/repositories, so this is a real interface, not a
    /// concrete-class-only registration (see ../CLAUDE.md's note on why this
    /// package doesn't follow that repo's "no new interfaces" rule).
    ///
    /// Tables are a simplified, relational-friendly reinterpretation of
    /// resources/database/clickhouse/otel-schema.sql (the SaaS platform's
    /// ClickHouse schema) -- same OTel-shaped core columns (Timestamp,
    /// ServiceName, SeverityText/Body for logs, SpanName/Duration/StatusCode
    /// for traces, MetricName/Value for metrics), but attribute maps become a
    /// single JSON text column instead of a native Map type (neither SQLite
    /// nor SQL Server has one), and the five separate ClickHouse metric-type
    /// tables (gauge/sum/histogram/exponential_histogram/summary) collapse
    /// into one nafas_metrics table with a MetricType discriminator --
    /// ClickHouse's MergeTree/bloom-filter/TTL machinery has no equivalent in
    /// either engine and isn't needed at this scale anyway.
    /// </summary>
    public interface INafasSchemaProvisioner
    {
        Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
    }
}
