using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Storage;

namespace Nafas.Observability.Ingestion
{
    /// <summary>
    /// Real log ingestion: an <see cref="ILoggerProvider"/>, registered by
    /// AddNafasServer via <c>services.AddSingleton&lt;ILoggerProvider,
    /// NafasLoggerProvider&gt;()</c>. Once registered, ASP.NET Core's default
    /// logging pipeline calls into this for every <c>ILogger&lt;T&gt;</c> log
    /// line the consuming app (or any library it uses) writes -- no separate
    /// opt-in call, no code changes in the app itself, exactly like adding
    /// Serilog/NLog/any other ILoggerProvider-based sink. This is what turns
    /// "the dashboard exists" into "the dashboard shows this app's own real
    /// logs".
    ///
    /// Every record is pushed to <see cref="NafasIngestionQueue"/> (never
    /// written to the database directly, and never blocking) -- see that
    /// class's own comment, and NafasIngestionWriterHostedService.cs for
    /// where it's drained.
    /// </summary>
    internal sealed class NafasLoggerProvider : ILoggerProvider
    {
        private readonly NafasIngestionQueue _queue;
        private readonly string _serviceName;

        public NafasLoggerProvider(NafasIngestionQueue queue, NafasServiceName serviceName)
        {
            _queue = queue;
            _serviceName = serviceName.Value;
        }

        public ILogger CreateLogger(string categoryName) => new NafasLogger(_queue, _serviceName, categoryName);

        public void Dispose() { }

        private sealed class NafasLogger : ILogger
        {
            private readonly NafasIngestionQueue _queue;
            private readonly string _serviceName;
            private readonly string _categoryName;

            public NafasLogger(NafasIngestionQueue queue, string serviceName, string categoryName)
            {
                _queue = queue;
                _serviceName = serviceName;
                _categoryName = categoryName;
            }

            // Always true -- the app's own logging:LogLevel configuration
            // (appsettings.json / code-based filters) already decides what
            // reaches ANY provider before this is asked; a second opinion
            // here would just silently diverge from whatever the app
            // configured elsewhere. Same posture Microsoft's own built-in
            // Console/Debug providers take.
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var body = formatter(state, exception);
                if (exception != null)
                {
                    body = string.IsNullOrEmpty(body) ? exception.ToString() : body + Environment.NewLine + exception;
                }

                // Correlates this log line to whatever span was active when
                // it was written, if any -- the same TraceId/SpanId a
                // request's own Activity gets in nafas_traces (see
                // NafasActivityIngestionHostedService.cs), letting the
                // dashboard eventually link "this log" to "this trace".
                var activity = Activity.Current;

                _queue.EnqueueLog(new NafasLogRecord
                {
                    Timestamp = DateTime.UtcNow,
                    ServiceName = _serviceName,
                    SeverityText = logLevel.ToString(),
                    SeverityNumber = ToOtelSeverityNumber(logLevel),
                    Body = body,
                    TraceId = activity?.TraceId.ToHexString(),
                    SpanId = activity?.SpanId.ToHexString(),
                    LogAttributes = JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["category"] = _categoryName,
                        ["eventId"] = eventId.Id,
                        ["eventName"] = eventId.Name,
                    }),
                });
            }

            // OTel's own SeverityNumber ranges (logs data model spec):
            // TRACE 1-4, DEBUG 5-8, INFO 9-12, WARN 13-16, ERROR 17-20,
            // FATAL 21-24. Using each range's first value, same as most
            // OTel language SDKs' default LogLevel mapping.
            private static int ToOtelSeverityNumber(LogLevel logLevel) => logLevel switch
            {
                LogLevel.Trace => 1,
                LogLevel.Debug => 5,
                LogLevel.Information => 9,
                LogLevel.Warning => 13,
                LogLevel.Error => 17,
                LogLevel.Critical => 21,
                _ => 0,
            };
        }
    }
}
