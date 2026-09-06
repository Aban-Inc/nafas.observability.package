using System;

namespace Nafas.Observability.Storage
{
    // Plain write-side records -- the mirror image of QueryModels.cs's
    // read-side DTOs. One column each, matching nafas_logs/nafas_metrics/
    // nafas_traces exactly (see INafasSchemaProvisioner's implementations),
    // produced by the Ingestion/ listeners and consumed by
    // INafasIngestionWriter. Internal: nothing outside this package ever
    // constructs or reads one directly.

    internal sealed class NafasLogRecord
    {
        public DateTime Timestamp { get; set; }
        public string ServiceName { get; set; } = "";
        public string? SeverityText { get; set; }
        public int? SeverityNumber { get; set; }
        public string? Body { get; set; }
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? ResourceAttributes { get; set; }
        public string? LogAttributes { get; set; }
    }

    internal sealed class NafasMetricRecord
    {
        public DateTime Timestamp { get; set; }
        public string ServiceName { get; set; } = "";
        public string MetricName { get; set; } = "";
        public string MetricType { get; set; } = "";
        public double? Value { get; set; }
        public string? Attributes { get; set; }
        public string? Payload { get; set; }
    }

    internal sealed class NafasTraceRecord
    {
        public DateTime Timestamp { get; set; }
        public string TraceId { get; set; } = "";
        public string SpanId { get; set; } = "";
        public string? ParentSpanId { get; set; }
        public string ServiceName { get; set; } = "";
        public string SpanName { get; set; } = "";
        public string? SpanKind { get; set; }
        public long? DurationNanos { get; set; }
        public string? StatusCode { get; set; }
        public string? StatusMessage { get; set; }
        public string? ResourceAttributes { get; set; }
        public string? SpanAttributes { get; set; }
    }
}
