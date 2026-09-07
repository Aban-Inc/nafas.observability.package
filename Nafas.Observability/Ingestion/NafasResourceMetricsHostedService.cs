using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Storage;

namespace Nafas.Observability.Ingestion
{
    /// <summary>
    /// Samples this process's own CPU and memory usage on a fixed interval
    /// and enqueues them as "cpu_usage"/"memory_usage" metrics (see
    /// SqliteQueryStore/SqlServerQueryStore's MetricNames -- both percent,
    /// 0-100). Unlike NafasMeterIngestionHostedService.cs, there's no
    /// existing .NET Meter that publishes these directly, so this is a
    /// small dedicated poller rather than a generic listener -- a native
    /// implementation using only <see cref="Process"/>/<see cref="GC"/>, no
    /// extra package, matching this package's zero-external-dependency goal.
    /// </summary>
    internal sealed class NafasResourceMetricsHostedService : BackgroundService
    {
        private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);

        private readonly NafasIngestionQueue _queue;
        private readonly NafasServiceName _serviceName;
        private readonly ILogger<NafasResourceMetricsHostedService> _logger;

        public NafasResourceMetricsHostedService(NafasIngestionQueue queue, NafasServiceName serviceName, ILogger<NafasResourceMetricsHostedService> logger)
        {
            _queue = queue;
            _serviceName = serviceName;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var process = Process.GetCurrentProcess();
            var lastSampleUtc = DateTime.UtcNow;
            var lastCpuTime = process.TotalProcessorTime;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(SampleInterval, stoppingToken).ConfigureAwait(false);

                    process.Refresh();
                    var nowUtc = DateTime.UtcNow;
                    var cpuTime = process.TotalProcessorTime;

                    // CPU%: how much of the available processor time (wall
                    // time * core count) this process actually used since the
                    // last sample -- the standard way to turn a cumulative
                    // TotalProcessorTime counter into an instantaneous
                    // percentage without an external profiling API.
                    var wallElapsed = (nowUtc - lastSampleUtc).TotalMilliseconds;
                    var cpuElapsed = (cpuTime - lastCpuTime).TotalMilliseconds;
                    var cpuPercent = wallElapsed > 0
                        ? Math.Min(100.0, cpuElapsed / (wallElapsed * Environment.ProcessorCount) * 100.0)
                        : 0.0;

                    // Memory%: working set against the total memory the GC
                    // considers available to this process -- container-aware
                    // (reflects a cgroup/Job Object memory limit when one's
                    // set, not just physical host RAM), which is exactly what
                    // "memory_usage" should mean for an app that might be
                    // running in a constrained container.
                    var totalAvailable = GetTotalAvailableMemoryBytes();
                    var memoryPercent = totalAvailable > 0
                        ? Math.Min(100.0, process.WorkingSet64 / (double)totalAvailable * 100.0)
                        : 0.0;

                    _queue.EnqueueMetric(new NafasMetricRecord
                    {
                        Timestamp = nowUtc,
                        ServiceName = _serviceName.Value,
                        MetricName = "cpu_usage",
                        MetricType = "Gauge",
                        Value = cpuPercent,
                    });
                    _queue.EnqueueMetric(new NafasMetricRecord
                    {
                        Timestamp = nowUtc,
                        ServiceName = _serviceName.Value,
                        MetricName = "memory_usage",
                        MetricType = "Gauge",
                        Value = memoryPercent,
                    });

                    lastSampleUtc = nowUtc;
                    lastCpuTime = cpuTime;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Same "never take down the consumer's app" posture as
                    // NafasRetentionHostedService's own sweep loop.
                    _logger.LogWarning(ex, "Nafas resource metrics sample failed; will retry at the next interval.");
                }
            }
        }

        // GC.GetGCMemoryInfo() isn't declared in netstandard2.0's reference
        // assembly at all -- it was added in .NET Core 3.0, and netstandard2.0
        // is also what classic .NET Framework 4.6.1+ can reference, where the
        // method genuinely does not exist at runtime (not just an API-surface
        // gap, unlike the netstandard2.1 case). Reflection is the standard,
        // safe way to call it when the host DOES have it (.NET Core 3.0+/.NET
        // 5+), and this falls back to 0 (memoryPercent becomes 0.0, never a
        // crash) on any host where it's genuinely missing, including real
        // .NET Framework hosts.
        private static long GetTotalAvailableMemoryBytes()
        {
            try
            {
                var method = typeof(GC).GetMethod("GetGCMemoryInfo", Type.EmptyTypes);
                var info = method?.Invoke(null, null);
                var property = info?.GetType().GetProperty("TotalAvailableMemoryBytes");
                return property != null ? (long)property.GetValue(info)! : 0L;
            }
            catch
            {
                return 0L;
            }
        }
    }
}
