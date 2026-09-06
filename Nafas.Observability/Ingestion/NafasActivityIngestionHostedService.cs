using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Storage;

namespace Nafas.Observability.Ingestion
{
    /// <summary>
    /// Real trace ingestion: an <see cref="ActivityListener"/> subscribed to
    /// every <see cref="ActivitySource"/> in the process. This is what
    /// actually produces spans with zero code changes in the consuming app --
    /// ASP.NET Core itself creates one <see cref="Activity"/> per incoming
    /// HTTP request (ActivitySource "Microsoft.AspNetCore"), so simply
    /// listening to everything already yields real per-request traces; any
    /// app-defined <c>ActivitySource</c> flows through the exact same path.
    ///
    /// Listening to every source (rather than a named allow-list) is a
    /// deliberate v1 simplification matching this package's zero-config
    /// goal -- there's no way to know in advance which ActivitySource names
    /// a given consuming app or its dependencies use. If this ever proves too
    /// noisy in practice (third-party library spans nobody wants), a
    /// name-filter option on NafasServerOptions would be the natural fix --
    /// not implemented here since nobody's asked for it yet.
    /// </summary>
    internal sealed class NafasActivityIngestionHostedService : IHostedService
    {
        private readonly NafasIngestionQueue _queue;
        private readonly NafasServiceName _serviceName;
        private readonly ILogger<NafasActivityIngestionHostedService> _logger;
        private ActivityListener? _listener;

        public NafasActivityIngestionHostedService(NafasIngestionQueue queue, NafasServiceName serviceName, ILogger<NafasActivityIngestionHostedService> logger)
        {
            _queue = queue;
            _serviceName = serviceName;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = OnActivityStopped,
            };
            ActivitySource.AddActivityListener(_listener);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _listener?.Dispose();
            return Task.CompletedTask;
        }

        private void OnActivityStopped(Activity activity)
        {
            // This callback runs synchronously inside the consuming app's own
            // call to activity.Stop()/Dispose() -- an unhandled exception
            // here would propagate straight into that app's code, not just
            // this package's. Swallow and log instead (same "never take down
            // the consumer's app" posture as NafasRetentionHostedService's
            // own try/catch), since a single unrecorded span is never worth
            // that risk. Most likely cause: a tag value JsonSerializer can't
            // handle.
            try
            {
                var attributes = new Dictionary<string, object?>();
                foreach (var tag in activity.TagObjects)
                {
                    attributes[tag.Key] = tag.Value;
                }

                _queue.EnqueueTrace(new NafasTraceRecord
                {
                    Timestamp = activity.StartTimeUtc,
                    TraceId = activity.TraceId.ToHexString(),
                    SpanId = activity.SpanId.ToHexString(),
                    ParentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
                    ServiceName = _serviceName.Value,
                    SpanName = activity.DisplayName,
                    SpanKind = activity.Kind.ToString(),
                    // 1 Activity.Duration tick = 100ns -- matches DurationNanos'
                    // documented unit (see INafasSchemaProvisioner's own comment
                    // and SqliteQueryStore.GetLatencyHeatmapAsync's nanoseconds
                    // -> milliseconds conversion on the read side).
                    DurationNanos = activity.Duration.Ticks * 100,
                    StatusCode = activity.Status.ToString(),
                    StatusMessage = activity.StatusDescription,
                    SpanAttributes = attributes.Count > 0 ? JsonSerializer.Serialize(attributes) : null,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nafas trace ingestion dropped one span ({SpanName}) after a serialization/enqueue error.", activity.DisplayName);
            }
        }
    }
}
