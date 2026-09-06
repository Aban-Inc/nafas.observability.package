using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Nafas.Observability.Storage
{
    internal sealed class SqlServerIngestionWriter : INafasIngestionWriter
    {
        private readonly string _connectionString;
        private readonly string _schema;

        // Schema was already validated against a strict identifier pattern by
        // SqlServerSchemaProvisioner at startup (same NafasServerOptions.Schema
        // value) -- safe to interpolate here for the same reason it's safe in
        // SqlServerQueryStore/NafasRetentionHostedService.
        public SqlServerIngestionWriter(string connectionString, string? schema)
        {
            _connectionString = connectionString;
            _schema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema!;
        }

        public async Task WriteLogsAsync(IReadOnlyList<NafasLogRecord> records, CancellationToken ct = default)
        {
            if (records.Count == 0) return;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"INSERT INTO [{_schema}].[nafas_logs]
([Timestamp], ServiceName, SeverityText, SeverityNumber, Body, TraceId, SpanId, ResourceAttributes, LogAttributes)
VALUES (@ts, @svc, @sev, @sevNum, @body, @traceId, @spanId, @resAttrs, @logAttrs);";
            var pTs = command.Parameters.Add("@ts", System.Data.SqlDbType.DateTime2);
            var pSvc = command.Parameters.Add("@svc", System.Data.SqlDbType.NVarChar, 200);
            var pSev = command.Parameters.Add("@sev", System.Data.SqlDbType.NVarChar, 50);
            var pSevNum = command.Parameters.Add("@sevNum", System.Data.SqlDbType.Int);
            var pBody = command.Parameters.Add("@body", System.Data.SqlDbType.NVarChar, -1);
            var pTraceId = command.Parameters.Add("@traceId", System.Data.SqlDbType.NVarChar, 64);
            var pSpanId = command.Parameters.Add("@spanId", System.Data.SqlDbType.NVarChar, 32);
            var pResAttrs = command.Parameters.Add("@resAttrs", System.Data.SqlDbType.NVarChar, -1);
            var pLogAttrs = command.Parameters.Add("@logAttrs", System.Data.SqlDbType.NVarChar, -1);

            foreach (var r in records)
            {
                pTs.Value = r.Timestamp.ToUniversalTime();
                pSvc.Value = r.ServiceName;
                pSev.Value = (object?)r.SeverityText ?? System.DBNull.Value;
                pSevNum.Value = (object?)r.SeverityNumber ?? System.DBNull.Value;
                pBody.Value = (object?)r.Body ?? System.DBNull.Value;
                pTraceId.Value = (object?)r.TraceId ?? System.DBNull.Value;
                pSpanId.Value = (object?)r.SpanId ?? System.DBNull.Value;
                pResAttrs.Value = (object?)r.ResourceAttributes ?? System.DBNull.Value;
                pLogAttrs.Value = (object?)r.LogAttributes ?? System.DBNull.Value;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            transaction.Commit();
        }

        public async Task WriteMetricsAsync(IReadOnlyList<NafasMetricRecord> records, CancellationToken ct = default)
        {
            if (records.Count == 0) return;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"INSERT INTO [{_schema}].[nafas_metrics]
([Timestamp], ServiceName, MetricName, MetricType, Value, Attributes, Payload)
VALUES (@ts, @svc, @name, @type, @value, @attrs, @payload);";
            var pTs = command.Parameters.Add("@ts", System.Data.SqlDbType.DateTime2);
            var pSvc = command.Parameters.Add("@svc", System.Data.SqlDbType.NVarChar, 200);
            var pName = command.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 200);
            var pType = command.Parameters.Add("@type", System.Data.SqlDbType.NVarChar, 50);
            var pValue = command.Parameters.Add("@value", System.Data.SqlDbType.Float);
            var pAttrs = command.Parameters.Add("@attrs", System.Data.SqlDbType.NVarChar, -1);
            var pPayload = command.Parameters.Add("@payload", System.Data.SqlDbType.NVarChar, -1);

            foreach (var r in records)
            {
                pTs.Value = r.Timestamp.ToUniversalTime();
                pSvc.Value = r.ServiceName;
                pName.Value = r.MetricName;
                pType.Value = r.MetricType;
                pValue.Value = (object?)r.Value ?? System.DBNull.Value;
                pAttrs.Value = (object?)r.Attributes ?? System.DBNull.Value;
                pPayload.Value = (object?)r.Payload ?? System.DBNull.Value;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            transaction.Commit();
        }

        public async Task WriteTracesAsync(IReadOnlyList<NafasTraceRecord> records, CancellationToken ct = default)
        {
            if (records.Count == 0) return;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"INSERT INTO [{_schema}].[nafas_traces]
([Timestamp], TraceId, SpanId, ParentSpanId, ServiceName, SpanName, SpanKind, DurationNanos, StatusCode, StatusMessage, ResourceAttributes, SpanAttributes)
VALUES (@ts, @traceId, @spanId, @parentSpanId, @svc, @spanName, @spanKind, @durationNanos, @statusCode, @statusMessage, @resAttrs, @spanAttrs);";
            var pTs = command.Parameters.Add("@ts", System.Data.SqlDbType.DateTime2);
            var pTraceId = command.Parameters.Add("@traceId", System.Data.SqlDbType.NVarChar, 64);
            var pSpanId = command.Parameters.Add("@spanId", System.Data.SqlDbType.NVarChar, 32);
            var pParentSpanId = command.Parameters.Add("@parentSpanId", System.Data.SqlDbType.NVarChar, 32);
            var pSvc = command.Parameters.Add("@svc", System.Data.SqlDbType.NVarChar, 200);
            var pSpanName = command.Parameters.Add("@spanName", System.Data.SqlDbType.NVarChar, 200);
            var pSpanKind = command.Parameters.Add("@spanKind", System.Data.SqlDbType.NVarChar, 50);
            var pDurationNanos = command.Parameters.Add("@durationNanos", System.Data.SqlDbType.BigInt);
            var pStatusCode = command.Parameters.Add("@statusCode", System.Data.SqlDbType.NVarChar, 50);
            var pStatusMessage = command.Parameters.Add("@statusMessage", System.Data.SqlDbType.NVarChar, -1);
            var pResAttrs = command.Parameters.Add("@resAttrs", System.Data.SqlDbType.NVarChar, -1);
            var pSpanAttrs = command.Parameters.Add("@spanAttrs", System.Data.SqlDbType.NVarChar, -1);

            foreach (var r in records)
            {
                pTs.Value = r.Timestamp.ToUniversalTime();
                pTraceId.Value = r.TraceId;
                pSpanId.Value = r.SpanId;
                pParentSpanId.Value = (object?)r.ParentSpanId ?? System.DBNull.Value;
                pSvc.Value = r.ServiceName;
                pSpanName.Value = r.SpanName;
                pSpanKind.Value = (object?)r.SpanKind ?? System.DBNull.Value;
                pDurationNanos.Value = (object?)r.DurationNanos ?? System.DBNull.Value;
                pStatusCode.Value = (object?)r.StatusCode ?? System.DBNull.Value;
                pStatusMessage.Value = (object?)r.StatusMessage ?? System.DBNull.Value;
                pResAttrs.Value = (object?)r.ResourceAttributes ?? System.DBNull.Value;
                pSpanAttrs.Value = (object?)r.SpanAttributes ?? System.DBNull.Value;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            transaction.Commit();
        }
    }
}
