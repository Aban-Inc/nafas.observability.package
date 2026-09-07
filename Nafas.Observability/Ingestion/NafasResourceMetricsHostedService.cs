using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
        // 5+), and this falls back to the Windows API path below (or 0, if
        // even that doesn't apply) on any host where it's genuinely missing.
        private static long GetTotalAvailableMemoryBytes()
        {
            var viaGc = GetTotalAvailableMemoryBytesViaGc();
            if (viaGc > 0)
            {
                return viaGc;
            }

            // Only reached on a host with no GC.GetGCMemoryInfo() -- classic
            // .NET Framework, or .NET Core 2.x. Classic .NET Framework only
            // ever runs on Windows, so GlobalMemoryStatusEx (the Win32 API
            // every .NET Framework app has always used for this, since no
            // managed equivalent exists there) covers exactly that gap.
            // Gated on IsOSPlatform(Windows) rather than just try/catching
            // the P/Invoke itself: this assembly is one single netstandard2.0
            // DLL shared by every consumer, including Linux/macOS and Docker
            // Linux-container hosts on .NET Core -- this check makes sure
            // GlobalMemoryStatusEx (a kernel32.dll export) is never even
            // attempted there, not just that a failure is caught.
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? GetTotalPhysicalMemoryBytesViaWindowsApi()
                : 0L;
        }

        private static long GetTotalAvailableMemoryBytesViaGc()
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

        // Whole-machine physical memory, not container/Job-Object-aware the
        // way GC.GetGCMemoryInfo() above is -- a process running under a Job
        // Object memory cap will see more "available" memory here than it can
        // actually use, so memory_usage under-reports in that specific case.
        // Still a real, useful number instead of the 0 this metric would
        // otherwise report on every classic .NET Framework host, and a
        // Job-Object-capped classic .NET Framework process is a narrow case
        // even within classic .NET Framework hosting as a whole.
        private static long GetTotalPhysicalMemoryBytesViaWindowsApi()
        {
            try
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0L;
            }
            catch
            {
                // Defensive: IsOSPlatform(Windows) above should already rule
                // out DllNotFoundException, but a locked-down environment
                // (e.g. missing kernel32 export, unlikely as that is) still
                // shouldn't take the sampler down.
                return 0L;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
