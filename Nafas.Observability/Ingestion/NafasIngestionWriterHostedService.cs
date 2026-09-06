using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Storage;

namespace Nafas.Observability.Ingestion
{
    /// <summary>
    /// Drains NafasIngestionQueue's three channels and batch-inserts into
    /// storage -- the one place that actually talks to the database for
    /// ingestion (every listener in this folder only ever enqueues, never
    /// writes directly). Batching, not one INSERT per record, for the same
    /// reason INafasIngestionWriter's own comment gives: ingestion volume
    /// makes per-row round trips far too slow.
    ///
    /// Also the one place that publishes newly-ingested logs to
    /// NafasLiveFeed's "logs" channel -- i.e. the dashboard's live log tail
    /// is fed from here, at write time, never by re-reading the database
    /// (same rule NafasLiveFeed.cs's own comment documents). Metrics/traces
    /// have no live stream today (see NafasDashboardEndpoints.cs's route
    /// table -- only api/logs/stream and api/alerts/stream exist), so those
    /// two are storage-only.
    /// </summary>
    internal sealed class NafasIngestionWriterHostedService : BackgroundService
    {
        // Small and frequent, not large and rare -- keeps the dashboard's
        // live log tail feeling instant and bounds how much would be lost if
        // the process died between drains, at the cost of more (still
        // batched, still cheap) INSERT statements than a longer window would
        // need.
        private const int MaxBatchSize = 500;
        private static readonly TimeSpan MaxBatchWait = TimeSpan.FromSeconds(1);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly NafasIngestionQueue _queue;
        private readonly INafasIngestionWriter _writer;
        private readonly NafasLiveFeed _liveFeed;
        private readonly ILogger<NafasIngestionWriterHostedService> _logger;

        public NafasIngestionWriterHostedService(NafasIngestionQueue queue, INafasIngestionWriter writer, NafasLiveFeed liveFeed, ILogger<NafasIngestionWriterHostedService> logger)
        {
            _queue = queue;
            _writer = writer;
            _liveFeed = liveFeed;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
            DrainLogsAsync(stoppingToken),
            DrainMetricsAsync(stoppingToken),
            DrainTracesAsync(stoppingToken));

        private async Task DrainLogsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var batch = await ReadBatchAsync(_queue.Logs.Reader, MaxBatchSize, MaxBatchWait, ct).ConfigureAwait(false);
                if (batch.Count == 0) continue;

                try
                {
                    await _writer.WriteLogsAsync(batch, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nafas log ingestion dropped a batch of {Count} record(s) after a write failure.", batch.Count);
                }

                foreach (var record in batch)
                {
                    _liveFeed.Publish("logs", BuildOtlpLogJson(record));
                }
            }
        }

        private async Task DrainMetricsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var batch = await ReadBatchAsync(_queue.Metrics.Reader, MaxBatchSize, MaxBatchWait, ct).ConfigureAwait(false);
                if (batch.Count == 0) continue;

                try
                {
                    await _writer.WriteMetricsAsync(batch, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nafas metric ingestion dropped a batch of {Count} record(s) after a write failure.", batch.Count);
                }
            }
        }

        private async Task DrainTracesAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var batch = await ReadBatchAsync(_queue.Traces.Reader, MaxBatchSize, MaxBatchWait, ct).ConfigureAwait(false);
                if (batch.Count == 0) continue;

                try
                {
                    await _writer.WriteTracesAsync(batch, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nafas trace ingestion dropped a batch of {Count} record(s) after a write failure.", batch.Count);
                }
            }
        }

        /// <summary>
        /// Collects up to <paramref name="maxBatchSize"/> items, waiting up
        /// to <paramref name="maxWait"/> total for them to arrive -- returns
        /// early (possibly empty) once either limit is hit, or once
        /// <paramref name="ct"/> is cancelled. A plain
        /// <c>reader.WaitToReadAsync</c> loop rather than
        /// <c>System.Threading.Channels</c> having any built-in batching
        /// primitive, because it doesn't have one.
        /// </summary>
        private static async Task<List<T>> ReadBatchAsync<T>(ChannelReader<T> reader, int maxBatchSize, TimeSpan maxWait, CancellationToken ct)
        {
            var batch = new List<T>(maxBatchSize);

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waitCts.CancelAfter(maxWait);

            try
            {
                while (batch.Count < maxBatchSize)
                {
                    if (!await reader.WaitToReadAsync(waitCts.Token).ConfigureAwait(false)) break; // channel completed
                    while (batch.Count < maxBatchSize && reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // maxWait elapsed with no (or a partial) batch -- return
                // whatever was collected so far, same as a real timeout.
            }

            return batch;
        }

        // Matches ClientApp/src/composables/useLogStream.ts's expected shape
        // exactly (a raw OTLP ExportLogsServiceRequest) -- see that file's
        // own comment for why it's this shape and not LogEntryViewModel, and
        // NafasDashboardEndpoints.EmitDebugLog for the same construction used
        // by the temporary debug endpoint this makes redundant.
        private static string BuildOtlpLogJson(NafasLogRecord record)
        {
            var timeUnixNano = (new DateTimeOffset(DateTime.SpecifyKind(record.Timestamp, DateTimeKind.Utc)).ToUnixTimeMilliseconds() * 1_000_000L)
                .ToString(CultureInfo.InvariantCulture);

            var payload = new
            {
                resourceLogs = new[]
                {
                    new
                    {
                        resource = new { attributes = new[] { new { key = "service.name", value = new { stringValue = record.ServiceName } } } },
                        scopeLogs = new[]
                        {
                            new
                            {
                                logRecords = new[]
                                {
                                    new
                                    {
                                        timeUnixNano,
                                        severityText = record.SeverityText ?? "",
                                        body = new { stringValue = record.Body ?? "" },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }
    }
}
