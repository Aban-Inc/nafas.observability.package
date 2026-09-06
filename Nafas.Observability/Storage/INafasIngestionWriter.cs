using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nafas.Observability.Storage
{
    /// <summary>
    /// The write side of nafas_logs/nafas_metrics/nafas_traces -- the mirror
    /// image of <see cref="INafasQueryStore"/> (which only reads them, plus
    /// alert-rule CRUD). Kept as a separate interface rather than added to
    /// INafasQueryStore because the two have completely different callers:
    /// this one is only ever called by Ingestion/NafasIngestionWriterHostedService,
    /// never by a dashboard request. One implementation per
    /// <see cref="DatabaseProvider"/>, same reasoning as every other
    /// Storage/ interface here.
    ///
    /// Every method takes a batch, not a single record -- ingestion volume
    /// (one call per log line / span / metric measurement) makes a
    /// round-trip-per-row insert pattern far too slow; see
    /// NafasIngestionWriterHostedService.cs for how batches are formed.
    /// </summary>
    internal interface INafasIngestionWriter
    {
        Task WriteLogsAsync(IReadOnlyList<NafasLogRecord> records, CancellationToken ct = default);
        Task WriteMetricsAsync(IReadOnlyList<NafasMetricRecord> records, CancellationToken ct = default);
        Task WriteTracesAsync(IReadOnlyList<NafasTraceRecord> records, CancellationToken ct = default);
    }
}
