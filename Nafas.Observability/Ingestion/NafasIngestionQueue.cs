using System.Threading.Channels;
using Nafas.Observability.Storage;

namespace Nafas.Observability.Ingestion
{
    /// <summary>
    /// Buffers records produced by the listeners in this folder
    /// (NafasLoggerProvider, NafasActivityIngestionHostedService,
    /// NafasMeterIngestionHostedService, NafasResourceMetricsHostedService)
    /// until NafasIngestionWriterHostedService drains and batch-inserts them.
    /// One bounded channel per signal, same reasoning as NafasLiveFeed.cs's
    /// unbounded ones except bounded here on purpose: ingestion sits directly
    /// in the consuming app's own hot paths (every log call, every span,
    /// every metric measurement), so writing to it must never block that
    /// app's own threads. TryWrite drops the newest record instead of
    /// blocking (or throwing) when a channel is full -- losing telemetry
    /// under sustained overload is an acceptable failure mode; slowing down
    /// or crashing the host application is not (same "never take down the
    /// consumer's app" posture as NafasRetentionHostedService's own comment).
    /// </summary>
    internal sealed class NafasIngestionQueue
    {
        // 20,000 records/channel is generous slack for the ~1s drain interval
        // NafasIngestionWriterHostedService uses -- comfortably absorbs a
        // burst without needing to be configurable for a v1.
        private const int Capacity = 20_000;

        public Channel<NafasLogRecord> Logs { get; } = Channel.CreateBounded<NafasLogRecord>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });

        public Channel<NafasMetricRecord> Metrics { get; } = Channel.CreateBounded<NafasMetricRecord>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });

        public Channel<NafasTraceRecord> Traces { get; } = Channel.CreateBounded<NafasTraceRecord>(
            new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });

        public void EnqueueLog(NafasLogRecord record) => Logs.Writer.TryWrite(record);
        public void EnqueueMetric(NafasMetricRecord record) => Metrics.Writer.TryWrite(record);
        public void EnqueueTrace(NafasTraceRecord record) => Traces.Writer.TryWrite(record);
    }
}
