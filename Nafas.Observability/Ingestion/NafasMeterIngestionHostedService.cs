using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Storage;

namespace Nafas.Observability.Ingestion
{
    /// <summary>
    /// Real metric ingestion: a <see cref="MeterListener"/> subscribed to
    /// every <see cref="Meter"/>/instrument in the process -- same "listen to
    /// everything, zero config" reasoning as
    /// NafasActivityIngestionHostedService.cs's ActivityListener. Any
    /// app-defined Counter/Histogram/Gauge flows straight through under its
    /// own instrument name.
    ///
    /// One instrument gets special-cased: ASP.NET Core 8+ automatically
    /// records "http.server.request.duration" (seconds) on its own built-in
    /// "Microsoft.AspNetCore.Hosting" Meter for every request, with no
    /// instrumentation code required in the app. Renamed here to
    /// "http_server_duration" (SqliteQueryStore/SqlServerQueryStore's
    /// MetricNames.P99Latency -- see those files' own comments) so the
    /// dashboard's existing latency KPIs/charts get real numbers for any
    /// plain ASP.NET Core app, zero-config. The same measurements are also
    /// counted to derive "request_rate" (MetricNames.RequestRate), flushed
    /// every 15s -- there's no single built-in "requests/sec" instrument to
    /// read directly, but counting completions over a known window is
    /// equivalent.
    /// </summary>
    internal sealed class NafasMeterIngestionHostedService : IHostedService, IDisposable
    {
        private const string AspNetRequestDurationInstrument = "http.server.request.duration";
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(15);

        private readonly NafasIngestionQueue _queue;
        private readonly NafasServiceName _serviceName;
        private readonly ILogger<NafasMeterIngestionHostedService> _logger;

        private MeterListener? _listener;
        private Timer? _observableTimer;
        private Timer? _requestRateTimer;
        private long _requestCount;
        private DateTime _requestWindowStartUtc;

        public NafasMeterIngestionHostedService(NafasIngestionQueue queue, NafasServiceName serviceName, ILogger<NafasMeterIngestionHostedService> logger)
        {
            _queue = queue;
            _serviceName = serviceName;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _requestWindowStartUtc = DateTime.UtcNow;

            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) => listener.EnableMeasurementEvents(instrument),
            };
            // Meter instruments only ever report through one of these seven
            // numeric types (Counter<T>/Histogram<T>/Gauge<T>'s own generic
            // constraint) -- registering all seven is how you cover every
            // instrument regardless of which one a given app/library used.
            _listener.SetMeasurementEventCallback<byte>(OnMeasurement);
            _listener.SetMeasurementEventCallback<short>(OnMeasurement);
            _listener.SetMeasurementEventCallback<int>(OnMeasurement);
            _listener.SetMeasurementEventCallback<long>(OnMeasurement);
            _listener.SetMeasurementEventCallback<float>(OnMeasurement);
            _listener.SetMeasurementEventCallback<double>(OnMeasurement);
            _listener.SetMeasurementEventCallback<decimal>(OnMeasurement);
            _listener.Start();

            // Observable instruments (ObservableGauge/ObservableCounter/
            // ObservableUpDownCounter) only report a value when explicitly
            // polled -- this is that poll, on a fixed interval, same idea as
            // a Prometheus scrape but in-process.
            _observableTimer = new Timer(_ => _listener?.RecordObservableInstruments(), null, FlushInterval, FlushInterval);
            _requestRateTimer = new Timer(FlushRequestRate, null, FlushInterval, FlushInterval);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _observableTimer?.Dispose();
            _requestRateTimer?.Dispose();
            _listener?.Dispose();
        }

        private void OnMeasurement<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) where T : struct
        {
            // Runs synchronously on whatever thread recorded the
            // measurement -- app code, potentially a request thread. Same
            // "never take down the consumer's app" guard as
            // NafasActivityIngestionHostedService.OnActivityStopped.
            try
            {
                var metricName = instrument.Name;
                if (string.Equals(metricName, AspNetRequestDurationInstrument, StringComparison.Ordinal))
                {
                    metricName = "http_server_duration";
                    Interlocked.Increment(ref _requestCount);
                }

                Dictionary<string, object?>? attributes = null;
                if (tags.Length > 0)
                {
                    attributes = new Dictionary<string, object?>();
                    foreach (var tag in tags)
                    {
                        attributes[tag.Key] = tag.Value;
                    }
                }

                _queue.EnqueueMetric(new NafasMetricRecord
                {
                    Timestamp = DateTime.UtcNow,
                    ServiceName = _serviceName.Value,
                    MetricName = metricName,
                    MetricType = InstrumentTypeName(instrument),
                    Value = Convert.ToDouble(measurement),
                    Attributes = attributes != null ? JsonSerializer.Serialize(attributes) : null,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nafas metric ingestion dropped one measurement from instrument {Instrument}.", instrument.Name);
            }
        }

        // Instrument's own runtime type name, generic arity marker stripped
        // ("Counter`1" -> "Counter", "Histogram`1" -> "Histogram", etc.) --
        // matches nafas_metrics.MetricType's free-text convention (see
        // INafasSchemaProvisioner); no fixed enum needed since .NET's own
        // instrument kinds (Counter/Histogram/Gauge/UpDownCounter/
        // Observable*) are exactly this set already.
        private static string InstrumentTypeName(Instrument instrument)
        {
            var name = instrument.GetType().Name;
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }

        private void FlushRequestRate(object? state)
        {
            var count = Interlocked.Exchange(ref _requestCount, 0);
            var windowEnd = DateTime.UtcNow;
            var elapsedSeconds = Math.Max(1.0, (windowEnd - _requestWindowStartUtc).TotalSeconds);
            _requestWindowStartUtc = windowEnd;

            // Always emitted, even at zero -- a real "0 req/s" data point
            // during a quiet period is meaningful and matches how a
            // dashboard KPI/trend should behave; skipping it would make the
            // chart look like ingestion stopped instead of the app being idle.
            _queue.EnqueueMetric(new NafasMetricRecord
            {
                Timestamp = windowEnd,
                ServiceName = _serviceName.Value,
                MetricName = "request_rate",
                MetricType = "Gauge",
                Value = count / elapsedSeconds,
            });
        }
    }
}
