using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Nafas.Observability.Storage
{
    internal sealed class SqliteIngestionWriter : INafasIngestionWriter
    {
        private readonly string _connectionString;

        public SqliteIngestionWriter(string connectionString) => _connectionString = connectionString;

        private static string Iso(System.DateTime dt) => dt.ToUniversalTime().ToString("O");

        public async Task WriteLogsAsync(IReadOnlyList<NafasLogRecord> records, CancellationToken ct = default)
        {
            if (records.Count == 0) return;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO nafas_logs
(Timestamp, ServiceName, SeverityText, SeverityNumber, Body, TraceId, SpanId, ResourceAttributes, LogAttributes)
VALUES (@ts, @svc, @sev, @sevNum, @body, @traceId, @spanId, @resAttrs, @logAttrs);";
            // One prepared command reused for every row in the batch (values
            // swapped between executions) instead of building/parsing a new
            // command per row -- this is a hot path (every ILogger call in
            // the consuming app funnels through here), not a dashboard read.
            var pTs = command.Parameters.Add("@ts", SqliteType.Text);
            var pSvc = command.Parameters.Add("@svc", SqliteType.Text);
            var pSev = command.Parameters.Add("@sev", SqliteType.Text);
            var pSevNum = command.Parameters.Add("@sevNum", SqliteType.Integer);
            var pBody = command.Parameters.Add("@body", SqliteType.Text);
            var pTraceId = command.Parameters.Add("@traceId", SqliteType.Text);
            var pSpanId = command.Parameters.Add("@spanId", SqliteType.Text);
            var pResAttrs = command.Parameters.Add("@resAttrs", SqliteType.Text);
            var pLogAttrs = command.Parameters.Add("@logAttrs", SqliteType.Text);

            foreach (var r in records)
            {
                pTs.Value = Iso(r.Timestamp);
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

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO nafas_metrics
(Timestamp, ServiceName, MetricName, MetricType, Value, Attributes, Payload)
VALUES (@ts, @svc, @name, @type, @value, @attrs, @payload);";
            var pTs = command.Parameters.Add("@ts", SqliteType.Text);
            var pSvc = command.Parameters.Add("@svc", SqliteType.Text);
            var pName = command.Parameters.Add("@name", SqliteType.Text);
            var pType = command.Parameters.Add("@type", SqliteType.Text);
            var pValue = command.Parameters.Add("@value", SqliteType.Real);
            var pAttrs = command.Parameters.Add("@attrs", SqliteType.Text);
            var pPayload = command.Parameters.Add("@payload", SqliteType.Text);

            foreach (var r in records)
            {
                pTs.Value = Iso(r.Timestamp);
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

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO nafas_traces
(Timestamp, TraceId, SpanId, ParentSpanId, ServiceName, SpanName, SpanKind, DurationNanos, StatusCode, StatusMessage, ResourceAttributes, SpanAttributes)
VALUES (@ts, @traceId, @spanId, @parentSpanId, @svc, @spanName, @spanKind, @durationNanos, @statusCode, @statusMessage, @resAttrs, @spanAttrs);";
            var pTs = command.Parameters.Add("@ts", SqliteType.Text);
            var pTraceId = command.Parameters.Add("@traceId", SqliteType.Text);
            var pSpanId = command.Parameters.Add("@spanId", SqliteType.Text);
            var pParentSpanId = command.Parameters.Add("@parentSpanId", SqliteType.Text);
            var pSvc = command.Parameters.Add("@svc", SqliteType.Text);
            var pSpanName = command.Parameters.Add("@spanName", SqliteType.Text);
            var pSpanKind = command.Parameters.Add("@spanKind", SqliteType.Text);
            var pDurationNanos = command.Parameters.Add("@durationNanos", SqliteType.Integer);
            var pStatusCode = command.Parameters.Add("@statusCode", SqliteType.Text);
            var pStatusMessage = command.Parameters.Add("@statusMessage", SqliteType.Text);
            var pResAttrs = command.Parameters.Add("@resAttrs", SqliteType.Text);
            var pSpanAttrs = command.Parameters.Add("@spanAttrs", SqliteType.Text);

            foreach (var r in records)
            {
                pTs.Value = Iso(r.Timestamp);
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
