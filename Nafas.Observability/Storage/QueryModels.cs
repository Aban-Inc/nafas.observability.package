using System.Collections.Generic;

namespace Nafas.Observability.Storage
{
    // Mirrors ClientApp/src/models/dashboard.vm.ts and alert.vm.ts exactly --
    // property names here become camelCase JSON automatically (ASP.NET Core's
    // Minimal API default), matching what the frontend already expects
    // without needing any [JsonPropertyName] attributes.

    public sealed class ErrorRateKpi { public string From { get; set; } = ""; public string To { get; set; } = ""; public long ErrorCount { get; set; } }
    public sealed class LogVolumeKpi { public string From { get; set; } = ""; public string To { get; set; } = ""; public long LogCount { get; set; } }

    public sealed class VolumeOverTimePoint { public string Timestamp { get; set; } = ""; public long Count { get; set; } }
    public sealed class VolumeOverTime { public string From { get; set; } = ""; public string To { get; set; } = ""; public int Interval { get; set; } public List<VolumeOverTimePoint> Data { get; set; } = new(); }

    public sealed class ServiceErrorRateEntry { public string ServiceName { get; set; } = ""; public long ErrorCount { get; set; } public long TotalCount { get; set; } }
    public sealed class ErrorRateByService { public string From { get; set; } = ""; public string To { get; set; } = ""; public List<ServiceErrorRateEntry> Data { get; set; } = new(); }

    public sealed class MetricEndpointsKpi { public string From { get; set; } = ""; public string To { get; set; } = ""; public int Healthy { get; set; } public int Total { get; set; } public int Scraping { get; set; } public List<double> Trend { get; set; } = new(); }

    public sealed class ActiveTracesKpi { public string From { get; set; } = ""; public string To { get; set; } = ""; public long Count { get; set; } public double PercentageChange { get; set; } public List<double> Trend { get; set; } = new(); }

    public sealed class LogEntry { public string Timestamp { get; set; } = ""; public string SeverityText { get; set; } = ""; public string ServiceName { get; set; } = ""; public string Body { get; set; } = ""; public string TraceId { get; set; } = ""; public string SpanId { get; set; } = ""; }
    public sealed class LogSearch { public string From { get; set; } = ""; public string To { get; set; } = ""; public int Page { get; set; } public int PageSize { get; set; } public long TotalCount { get; set; } public List<LogEntry> Data { get; set; } = new(); }

    public sealed class MetricsOverviewKpi
    {
        public string From { get; set; } = ""; public string To { get; set; } = "";
        public double CpuUsage { get; set; } public double CpuUsageChange { get; set; }
        public double MemoryUsage { get; set; } public double MemoryUsageChange { get; set; }
        public double RequestRate { get; set; } public double RequestRateChange { get; set; }
        public double P99Latency { get; set; } public double P99LatencyChange { get; set; }
    }

    public sealed class GaugeTrendPoint { public string Timestamp { get; set; } = ""; public double Value { get; set; } }
    public sealed class MetricTrend { public string From { get; set; } = ""; public string To { get; set; } = ""; public int Interval { get; set; } public List<GaugeTrendPoint> Data { get; set; } = new(); }

    public sealed class ServiceOverviewItem { public string ServiceName { get; set; } = ""; public double AvgCpu { get; set; } public double AvgMemory { get; set; } public double RequestRate { get; set; } public double ErrorRate { get; set; } }
    public sealed class ServiceOverview { public string From { get; set; } = ""; public string To { get; set; } = ""; public List<ServiceOverviewItem> Data { get; set; } = new(); }

    public sealed class TracesOverviewKpi
    {
        public string From { get; set; } = ""; public string To { get; set; } = "";
        public long TotalTraces { get; set; } public double TotalTracesChange { get; set; }
        public double AvgDuration { get; set; } public double AvgDurationChange { get; set; }
        public long ErrorTraces { get; set; } public double ErrorTracesChange { get; set; }
        public double P95Duration { get; set; } public double P95DurationChange { get; set; }
    }

    public sealed class TraceSummary { public string TraceId { get; set; } = ""; public string Timestamp { get; set; } = ""; public string ServiceName { get; set; } = ""; public string SpanName { get; set; } = ""; public long Duration { get; set; } public string StatusCode { get; set; } = ""; }
    public sealed class TraceSearch { public string From { get; set; } = ""; public string To { get; set; } = ""; public int Page { get; set; } public int PageSize { get; set; } public long TotalCount { get; set; } public List<TraceSummary> Data { get; set; } = new(); }

    public sealed class LatencyHeatmapPoint { public string Timestamp { get; set; } = ""; public double P50 { get; set; } public double P95 { get; set; } public double P99 { get; set; } }
    public sealed class LatencyHeatmap { public string From { get; set; } = ""; public string To { get; set; } = ""; public List<LatencyHeatmapPoint> Data { get; set; } = new(); }

    public sealed class ServiceCatalog { public string From { get; set; } = ""; public string To { get; set; } = ""; public List<string> Services { get; set; } = new(); }

    // ---- Alerts (mirrors alert.vm.ts) ----

    public sealed class AlertRule
    {
        public long Id { get; set; } public string Name { get; set; } = ""; public string RuleType { get; set; } = "";
        public string? Metric { get; set; } public string? ServiceName { get; set; } public double? ThresholdValue { get; set; }
        public int WindowMinutes { get; set; } public bool Enabled { get; set; } public string CreatedAt { get; set; } = "";
    }
    public sealed class CreateAlertRuleRequest { public string Name { get; set; } = ""; public string RuleType { get; set; } = ""; public string? Metric { get; set; } public string? ServiceName { get; set; } public double? ThresholdValue { get; set; } public int WindowMinutes { get; set; } }
    public sealed class UpdateAlertRuleRequest { public string Name { get; set; } = ""; public string? Metric { get; set; } public string? ServiceName { get; set; } public double? ThresholdValue { get; set; } public int WindowMinutes { get; set; } public bool Enabled { get; set; } }

    public sealed class AlertIncident
    {
        public long Id { get; set; } public long RuleId { get; set; } public string RuleName { get; set; } = ""; public string RuleType { get; set; } = "";
        public string Status { get; set; } = ""; public string? Detail { get; set; } public string TriggeredAt { get; set; } = ""; public string? ResolvedAt { get; set; }
    }
}
