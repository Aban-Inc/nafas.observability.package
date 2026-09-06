using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Nafas.Observability.Storage
{
    internal sealed class SqliteSchemaProvisioner : INafasSchemaProvisioner
    {
        private readonly string _connectionString;

        public SqliteSchemaProvisioner(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            // One batched script -- SQLite's ADO.NET provider runs multiple
            // ';'-separated statements from a single ExecuteNonQuery fine, and
            // "IF NOT EXISTS" makes every statement here safe to rerun on
            // every app startup without erroring on a table that's already
            // there. See INafasSchemaProvisioner.cs's own comment for why
            // these columns look the way they do relative to the ClickHouse
            // schema this is derived from.
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS nafas_logs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    ServiceName TEXT NOT NULL,
    SeverityText TEXT NULL,
    SeverityNumber INTEGER NULL,
    Body TEXT NULL,
    TraceId TEXT NULL,
    SpanId TEXT NULL,
    ResourceAttributes TEXT NULL,
    LogAttributes TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_nafas_logs_timestamp ON nafas_logs (Timestamp);
CREATE INDEX IF NOT EXISTS ix_nafas_logs_service_timestamp ON nafas_logs (ServiceName, Timestamp);

CREATE TABLE IF NOT EXISTS nafas_metrics (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    ServiceName TEXT NOT NULL,
    MetricName TEXT NOT NULL,
    MetricType TEXT NOT NULL,
    Value REAL NULL,
    Attributes TEXT NULL,
    Payload TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_nafas_metrics_timestamp ON nafas_metrics (Timestamp);
CREATE INDEX IF NOT EXISTS ix_nafas_metrics_service_metric_timestamp ON nafas_metrics (ServiceName, MetricName, Timestamp);

CREATE TABLE IF NOT EXISTS nafas_traces (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    TraceId TEXT NOT NULL,
    SpanId TEXT NOT NULL,
    ParentSpanId TEXT NULL,
    ServiceName TEXT NOT NULL,
    SpanName TEXT NOT NULL,
    SpanKind TEXT NULL,
    DurationNanos INTEGER NULL,
    StatusCode TEXT NULL,
    StatusMessage TEXT NULL,
    ResourceAttributes TEXT NULL,
    SpanAttributes TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_nafas_traces_trace_id ON nafas_traces (TraceId);
CREATE INDEX IF NOT EXISTS ix_nafas_traces_timestamp ON nafas_traces (Timestamp);
CREATE INDEX IF NOT EXISTS ix_nafas_traces_service_span_timestamp ON nafas_traces (ServiceName, SpanName, Timestamp);

CREATE TABLE IF NOT EXISTS nafas_alert_rules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    RuleType TEXT NOT NULL,
    Metric TEXT NULL,
    ServiceName TEXT NULL,
    ThresholdValue REAL NULL,
    WindowMinutes INTEGER NOT NULL,
    Enabled INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS nafas_alert_incidents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RuleId INTEGER NOT NULL REFERENCES nafas_alert_rules(Id),
    RuleName TEXT NOT NULL,
    RuleType TEXT NOT NULL,
    Status TEXT NOT NULL,
    Detail TEXT NULL,
    TriggeredAt TEXT NOT NULL,
    ResolvedAt TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_nafas_alert_incidents_rule_id ON nafas_alert_incidents (RuleId);
CREATE INDEX IF NOT EXISTS ix_nafas_alert_incidents_status ON nafas_alert_incidents (Status);
";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
