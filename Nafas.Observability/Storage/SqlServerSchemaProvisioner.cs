using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Nafas.Observability.Storage
{
    internal sealed class SqlServerSchemaProvisioner : INafasSchemaProvisioner
    {
        // Schema/table/index names below all end up interpolated directly into
        // DDL text -- SQL Server has no way to parameterize an identifier, only
        // values. Restricting NafasServerOptions.Schema to this pattern before
        // it ever reaches a SQL string is what keeps that safe.
        private static readonly Regex ValidIdentifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private readonly string _connectionString;
        private readonly string _schema;

        public SqlServerSchemaProvisioner(string connectionString, string? schema)
        {
            _connectionString = connectionString;
            _schema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema!;

            if (!ValidIdentifier.IsMatch(_schema))
            {
                throw new ArgumentException(
                    $"NafasServerOptions.Schema \"{schema}\" is not a valid SQL Server schema name " +
                    "(letters, digits and underscore only, can't start with a digit).",
                    nameof(schema));
            }
        }

        public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            if (!string.Equals(_schema, "dbo", StringComparison.OrdinalIgnoreCase))
            {
                // CREATE SCHEMA must be the only statement in its batch, so
                // this runs as dynamic SQL inside EXEC rather than sitting
                // directly in a multi-statement script like the table/index
                // DDL below.
                await ExecuteAsync(connection, $@"
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '{_schema}')
BEGIN
    EXEC('CREATE SCHEMA [{_schema}]');
END", cancellationToken).ConfigureAwait(false);
            }

            var script = new StringBuilder();

            script.Append(CreateTableIfNotExists("nafas_logs", @"
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    [Timestamp] DATETIME2 NOT NULL,
    ServiceName NVARCHAR(200) NOT NULL,
    SeverityText NVARCHAR(50) NULL,
    SeverityNumber INT NULL,
    Body NVARCHAR(MAX) NULL,
    TraceId NVARCHAR(64) NULL,
    SpanId NVARCHAR(32) NULL,
    ResourceAttributes NVARCHAR(MAX) NULL,
    LogAttributes NVARCHAR(MAX) NULL"));
            script.Append(CreateIndexIfNotExists("ix_nafas_logs_timestamp", "nafas_logs", "[Timestamp]"));
            script.Append(CreateIndexIfNotExists("ix_nafas_logs_service_timestamp", "nafas_logs", "ServiceName, [Timestamp]"));

            script.Append(CreateTableIfNotExists("nafas_metrics", @"
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    [Timestamp] DATETIME2 NOT NULL,
    ServiceName NVARCHAR(200) NOT NULL,
    MetricName NVARCHAR(200) NOT NULL,
    MetricType NVARCHAR(50) NOT NULL,
    Value FLOAT NULL,
    Attributes NVARCHAR(MAX) NULL,
    Payload NVARCHAR(MAX) NULL"));
            script.Append(CreateIndexIfNotExists("ix_nafas_metrics_timestamp", "nafas_metrics", "[Timestamp]"));
            script.Append(CreateIndexIfNotExists("ix_nafas_metrics_service_metric_timestamp", "nafas_metrics", "ServiceName, MetricName, [Timestamp]"));

            script.Append(CreateTableIfNotExists("nafas_traces", @"
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    [Timestamp] DATETIME2 NOT NULL,
    TraceId NVARCHAR(64) NOT NULL,
    SpanId NVARCHAR(32) NOT NULL,
    ParentSpanId NVARCHAR(32) NULL,
    ServiceName NVARCHAR(200) NOT NULL,
    SpanName NVARCHAR(200) NOT NULL,
    SpanKind NVARCHAR(50) NULL,
    DurationNanos BIGINT NULL,
    StatusCode NVARCHAR(50) NULL,
    StatusMessage NVARCHAR(MAX) NULL,
    ResourceAttributes NVARCHAR(MAX) NULL,
    SpanAttributes NVARCHAR(MAX) NULL"));
            script.Append(CreateIndexIfNotExists("ix_nafas_traces_trace_id", "nafas_traces", "TraceId"));
            script.Append(CreateIndexIfNotExists("ix_nafas_traces_timestamp", "nafas_traces", "[Timestamp]"));
            script.Append(CreateIndexIfNotExists("ix_nafas_traces_service_span_timestamp", "nafas_traces", "ServiceName, SpanName, [Timestamp]"));

            script.Append(CreateTableIfNotExists("nafas_alert_rules", @"
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    RuleType NVARCHAR(50) NOT NULL,
    Metric NVARCHAR(50) NULL,
    ServiceName NVARCHAR(200) NULL,
    ThresholdValue FLOAT NULL,
    WindowMinutes INT NOT NULL,
    Enabled BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL"));

            script.Append(CreateTableIfNotExists("nafas_alert_incidents", $@"
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleId BIGINT NOT NULL REFERENCES [{_schema}].nafas_alert_rules(Id),
    RuleName NVARCHAR(200) NOT NULL,
    RuleType NVARCHAR(50) NOT NULL,
    Status NVARCHAR(20) NOT NULL,
    Detail NVARCHAR(MAX) NULL,
    TriggeredAt DATETIME2 NOT NULL,
    ResolvedAt DATETIME2 NULL"));
            script.Append(CreateIndexIfNotExists("ix_nafas_alert_incidents_rule_id", "nafas_alert_incidents", "RuleId"));
            script.Append(CreateIndexIfNotExists("ix_nafas_alert_incidents_status", "nafas_alert_incidents", "Status"));

            await ExecuteAsync(connection, script.ToString(), cancellationToken).ConfigureAwait(false);
        }

        private string CreateTableIfNotExists(string tableName, string columnsDdl) => $@"
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = '{tableName}' AND s.name = '{_schema}')
BEGIN
    CREATE TABLE [{_schema}].[{tableName}] ({columnsDdl}
    );
END
";

        private string CreateIndexIfNotExists(string indexName, string tableName, string columns) => $@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{indexName}' AND object_id = OBJECT_ID('[{_schema}].[{tableName}]'))
BEGIN
    CREATE INDEX [{indexName}] ON [{_schema}].[{tableName}] ({columns});
END
";

        private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
