using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Nafas.Observability.Storage
{
    /// <summary>
    /// Time-series/percentile queries (VolumeOverTime, MetricTrend,
    /// LatencyHeatmap) fetch just the columns needed and bucket/aggregate in
    /// C#, rather than provider-specific date-truncation SQL -- simpler, and
    /// identical logic works for both SqliteQueryStore and
    /// SqlServerQueryStore. Fine at this scale (a single embedded app's own
    /// telemetry, not a multi-tenant SaaS's); revisit if that stops being
    /// true.
    ///
    /// Metric-name conventions (MetricsOverviewKpi's cpuUsage/memoryUsage/
    /// requestRate/p99Latency, MetricEndpointsKpi's healthy/scraping) are
    /// queried by fixed MetricName strings (see MetricNames below) that
    /// nothing actually emits yet -- ingestion (not built yet) needs to use
    /// these exact names for these KPIs to ever show real data. Documented
    /// here rather than guessed silently.
    /// </summary>
    internal sealed class SqliteQueryStore : INafasQueryStore
    {
        private static class MetricNames
        {
            public const string CpuUsage = "cpu_usage"; // percent, 0-100
            public const string MemoryUsage = "memory_usage"; // percent, 0-100
            public const string RequestRate = "request_rate"; // requests/sec
            // Seconds, not milliseconds -- OTel's own duration convention
            // (e.g. http.server.duration), which real ingestion will emit in
            // natively. The dashboard multiplies by 1000 for display
            // (ClientApp/src/pages/metrics.vue's formattedP99Latency) --
            // learned the hard way seeding demo data in milliseconds by
            // mistake produced a nonsense "176452 ms" reading.
            public const string P99Latency = "http_server_duration";
        }

        private readonly string _connectionString;

        public SqliteQueryStore(string connectionString) => _connectionString = connectionString;

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("O");

        public async Task<ErrorRateKpi> GetErrorRateKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            var count = await ScalarLongAsync(connection,
                "SELECT COUNT(*) FROM nafas_logs WHERE Timestamp >= @from AND Timestamp < @to AND UPPER(SeverityText) = 'ERROR'" + ServiceFilter(serviceName),
                Params(from, to, serviceName), ct).ConfigureAwait(false);
            return new ErrorRateKpi { From = Iso(from), To = Iso(to), ErrorCount = count };
        }

        public async Task<LogVolumeKpi> GetLogVolumeKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            var count = await ScalarLongAsync(connection,
                "SELECT COUNT(*) FROM nafas_logs WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName),
                Params(from, to, serviceName), ct).ConfigureAwait(false);
            return new LogVolumeKpi { From = Iso(from), To = Iso(to), LogCount = count };
        }

        public async Task<VolumeOverTime> GetVolumeOverTimeAsync(DateTime from, DateTime to, int intervalMinutes, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Timestamp FROM nafas_logs WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName);
            AddParams(command, Params(from, to, serviceName));

            var timestamps = new List<DateTime>();
            using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    timestamps.Add(DateTime.Parse(reader.GetString(0)).ToUniversalTime());
                }
            }

            return new VolumeOverTime
            {
                From = Iso(from),
                To = Iso(to),
                Interval = intervalMinutes,
                Data = BucketTimestamps(timestamps, from, to, intervalMinutes).Select(b => new VolumeOverTimePoint { Timestamp = Iso(b.BucketStart), Count = b.Values.Count }).ToList(),
            };
        }

        public async Task<ErrorRateByService> GetErrorRateByServiceAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT ServiceName,
       SUM(CASE WHEN UPPER(SeverityText) = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount,
       COUNT(*) AS TotalCount
FROM nafas_logs
WHERE Timestamp >= @from AND Timestamp < @to
GROUP BY ServiceName
ORDER BY ErrorCount DESC;";
            AddParams(command, Params(from, to, null));

            var data = new List<ServiceErrorRateEntry>();
            using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    data.Add(new ServiceErrorRateEntry { ServiceName = reader.GetString(0), ErrorCount = reader.GetInt64(1), TotalCount = reader.GetInt64(2) });
                }
            }

            return new ErrorRateByService { From = Iso(from), To = Iso(to), Data = data };
        }

        public async Task<LogSearch> SearchLogsAsync(DateTime from, DateTime to, string? level, string? serviceName, string? search, int page, int pageSize, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(200, pageSize));

            using var connection = Open();

            var where = "WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName);
            if (!string.IsNullOrWhiteSpace(level)) where += " AND UPPER(SeverityText) = @level";
            if (!string.IsNullOrWhiteSpace(search)) where += " AND Body LIKE @search";

            using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = $"SELECT COUNT(*) FROM nafas_logs {where};";
                AddSearchParams(countCommand, from, to, serviceName, level, search);
                var totalCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));

                using var dataCommand = connection.CreateCommand();
                dataCommand.CommandText = $@"
SELECT Timestamp, SeverityText, ServiceName, Body, TraceId, SpanId
FROM nafas_logs {where}
ORDER BY Timestamp DESC
LIMIT @take OFFSET @skip;";
                AddSearchParams(dataCommand, from, to, serviceName, level, search);
                dataCommand.Parameters.AddWithValue("@take", pageSize);
                dataCommand.Parameters.AddWithValue("@skip", (page - 1) * pageSize);

                var data = new List<LogEntry>();
                using (var reader = await dataCommand.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        data.Add(new LogEntry
                        {
                            Timestamp = reader.GetString(0),
                            SeverityText = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            ServiceName = reader.GetString(2),
                            Body = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            TraceId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            SpanId = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        });
                    }
                }

                return new LogSearch { From = Iso(from), To = Iso(to), Page = page, PageSize = pageSize, TotalCount = totalCount, Data = data };
            }
        }

        public async Task<MetricEndpointsKpi> GetMetricEndpointsKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            var distinctServices = await ScalarLongAsync(connection,
                "SELECT COUNT(DISTINCT ServiceName) FROM nafas_metrics WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName),
                Params(from, to, serviceName), ct).ConfigureAwait(false);

            // "Healthy"/"scraping" have no real meaning for push-based
            // ingestion (there's no scrape target to be up/down) -- every
            // distinct service that reported *any* metric in range counts as
            // both, until there's a real health signal to key off of.
            var total = (int)distinctServices;
            return new MetricEndpointsKpi { From = Iso(from), To = Iso(to), Healthy = total, Total = total, Scraping = total, Trend = new List<double>() };
        }

        public async Task<MetricsOverviewKpi> GetMetricsOverviewKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            var (cpu, cpuPrev) = await AvgCurrentAndPreviousAsync(connection, MetricNames.CpuUsage, from, to, serviceName, ct).ConfigureAwait(false);
            var (mem, memPrev) = await AvgCurrentAndPreviousAsync(connection, MetricNames.MemoryUsage, from, to, serviceName, ct).ConfigureAwait(false);
            var (req, reqPrev) = await AvgCurrentAndPreviousAsync(connection, MetricNames.RequestRate, from, to, serviceName, ct).ConfigureAwait(false);
            var (p99, p99Prev) = await AvgCurrentAndPreviousAsync(connection, MetricNames.P99Latency, from, to, serviceName, ct).ConfigureAwait(false);

            return new MetricsOverviewKpi
            {
                From = Iso(from), To = Iso(to),
                CpuUsage = cpu, CpuUsageChange = PercentChange(cpu, cpuPrev),
                MemoryUsage = mem, MemoryUsageChange = PercentChange(mem, memPrev),
                RequestRate = req, RequestRateChange = PercentChange(req, reqPrev),
                P99Latency = p99, P99LatencyChange = PercentChange(p99, p99Prev),
            };
        }

        public async Task<MetricTrend> GetMetricTrendAsync(DateTime from, DateTime to, int intervalMinutes, string metricName, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Timestamp, Value FROM nafas_metrics WHERE Timestamp >= @from AND Timestamp < @to AND MetricName = @metric" + ServiceFilter(serviceName);
            AddParams(command, Params(from, to, serviceName));
            command.Parameters.AddWithValue("@metric", metricName);

            var points = new List<(DateTime Timestamp, double Value)>();
            using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (reader.IsDBNull(1)) continue;
                    points.Add((DateTime.Parse(reader.GetString(0)).ToUniversalTime(), reader.GetDouble(1)));
                }
            }

            var buckets = BucketValues(points, from, to, intervalMinutes);
            return new MetricTrend
            {
                From = Iso(from), To = Iso(to), Interval = intervalMinutes,
                Data = buckets.Select(b => new GaugeTrendPoint { Timestamp = Iso(b.BucketStart), Value = b.Values.Count > 0 ? b.Values.Average() : 0 }).ToList(),
            };
        }

        public async Task<ServiceOverview> GetServiceOverviewAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            using var connection = Open();
            var services = await DistinctServiceNamesAsync(connection, "nafas_metrics", from, to, ct).ConfigureAwait(false);

            var data = new List<ServiceOverviewItem>();
            foreach (var service in services)
            {
                var cpu = await AvgAsync(connection, MetricNames.CpuUsage, from, to, service, ct).ConfigureAwait(false);
                var mem = await AvgAsync(connection, MetricNames.MemoryUsage, from, to, service, ct).ConfigureAwait(false);
                var req = await AvgAsync(connection, MetricNames.RequestRate, from, to, service, ct).ConfigureAwait(false);

                var totalLogs = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM nafas_logs WHERE Timestamp >= @from AND Timestamp < @to AND ServiceName = @service", Params(from, to, service), ct).ConfigureAwait(false);
                var errorLogs = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM nafas_logs WHERE Timestamp >= @from AND Timestamp < @to AND ServiceName = @service AND UPPER(SeverityText) = 'ERROR'", Params(from, to, service), ct).ConfigureAwait(false);

                data.Add(new ServiceOverviewItem
                {
                    ServiceName = service, AvgCpu = cpu, AvgMemory = mem, RequestRate = req,
                    ErrorRate = totalLogs > 0 ? (double)errorLogs / totalLogs * 100 : 0,
                });
            }

            return new ServiceOverview { From = Iso(from), To = Iso(to), Data = data };
        }

        public async Task<ActiveTracesKpi> GetActiveTracesKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            var count = await ScalarLongAsync(connection,
                "SELECT COUNT(DISTINCT TraceId) FROM nafas_traces WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName),
                Params(from, to, serviceName), ct).ConfigureAwait(false);

            var (_, span) = (from, to - from);
            var prevFrom = from - span; var prevTo = from;
            var prevCount = await ScalarLongAsync(connection,
                "SELECT COUNT(DISTINCT TraceId) FROM nafas_traces WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName),
                Params(prevFrom, prevTo, serviceName), ct).ConfigureAwait(false);

            return new ActiveTracesKpi { From = Iso(from), To = Iso(to), Count = count, PercentageChange = PercentChange(count, prevCount), Trend = new List<double>() };
        }

        public async Task<TracesOverviewKpi> GetTracesOverviewKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();

            var (durations, errorCount, total) = await TraceStatsAsync(connection, from, to, serviceName, ct).ConfigureAwait(false);
            var span = to - from;
            var (prevDurations, prevErrorCount, prevTotal) = await TraceStatsAsync(connection, from - span, from, serviceName, ct).ConfigureAwait(false);

            var avg = durations.Count > 0 ? durations.Average() : 0;
            var prevAvg = prevDurations.Count > 0 ? prevDurations.Average() : 0;
            var p95 = Percentile(durations, 0.95);
            var prevP95 = Percentile(prevDurations, 0.95);

            return new TracesOverviewKpi
            {
                From = Iso(from), To = Iso(to),
                TotalTraces = total, TotalTracesChange = PercentChange(total, prevTotal),
                AvgDuration = avg, AvgDurationChange = PercentChange(avg, prevAvg),
                ErrorTraces = errorCount, ErrorTracesChange = PercentChange(errorCount, prevErrorCount),
                P95Duration = p95, P95DurationChange = PercentChange(p95, prevP95),
            };
        }

        public async Task<LatencyHeatmap> GetLatencyHeatmapAsync(DateTime from, DateTime to, int bucketHours, string? serviceName, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Timestamp, DurationNanos FROM nafas_traces WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName);
            AddParams(command, Params(from, to, serviceName));

            var points = new List<(DateTime Timestamp, double Value)>();
            using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    if (reader.IsDBNull(1)) continue;
                    // nanoseconds -> milliseconds: TraceLatencyHeatmap.vue's
                    // formatMs()/cellColor() both expect millisecond-range
                    // values, unlike TraceSearch's raw-nanosecond Duration
                    // field (traces.vue converts that one itself). Confirmed
                    // live: without this, seeded ~200ms traces rendered as
                    // "200000.0s" per cell, overflowing the heatmap grid.
                    points.Add((DateTime.Parse(reader.GetString(0)).ToUniversalTime(), reader.GetInt64(1) / 1_000_000.0));
                }
            }

            var buckets = BucketValues(points, from, to, Math.Max(1, bucketHours) * 60);
            return new LatencyHeatmap
            {
                From = Iso(from), To = Iso(to),
                Data = buckets.Select(b => new LatencyHeatmapPoint
                {
                    Timestamp = Iso(b.BucketStart),
                    P50 = Percentile(b.Values, 0.50),
                    P95 = Percentile(b.Values, 0.95),
                    P99 = Percentile(b.Values, 0.99),
                }).ToList(),
            };
        }

        public async Task<TraceSearch> SearchTracesAsync(DateTime from, DateTime to, string? serviceName, string? traceId, int page, int pageSize, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(200, pageSize));

            using var connection = Open();

            var where = "WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName);
            if (!string.IsNullOrWhiteSpace(traceId)) where += " AND TraceId = @traceId";

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"SELECT COUNT(*) FROM nafas_traces {where};";
            AddTraceSearchParams(countCommand, from, to, serviceName, traceId);
            var totalCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));

            using var dataCommand = connection.CreateCommand();
            dataCommand.CommandText = $@"
SELECT TraceId, Timestamp, ServiceName, SpanName, DurationNanos, StatusCode
FROM nafas_traces {where}
ORDER BY Timestamp DESC
LIMIT @take OFFSET @skip;";
            AddTraceSearchParams(dataCommand, from, to, serviceName, traceId);
            dataCommand.Parameters.AddWithValue("@take", pageSize);
            dataCommand.Parameters.AddWithValue("@skip", (page - 1) * pageSize);

            var data = new List<TraceSummary>();
            using (var reader = await dataCommand.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    data.Add(new TraceSummary
                    {
                        TraceId = reader.GetString(0),
                        Timestamp = reader.GetString(1),
                        ServiceName = reader.GetString(2),
                        SpanName = reader.GetString(3),
                        Duration = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                        StatusCode = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    });
                }
            }

            return new TraceSearch { From = Iso(from), To = Iso(to), Page = page, PageSize = pageSize, TotalCount = totalCount, Data = data };
        }

        public async Task<ServiceCatalog> ListServicesAsync(DateTime from, DateTime to, CancellationToken ct = default)
        {
            using var connection = Open();
            var fromLogs = await DistinctServiceNamesAsync(connection, "nafas_logs", from, to, ct).ConfigureAwait(false);
            var fromTraces = await DistinctServiceNamesAsync(connection, "nafas_traces", from, to, ct).ConfigureAwait(false);
            var fromMetrics = await DistinctServiceNamesAsync(connection, "nafas_metrics", from, to, ct).ConfigureAwait(false);
            var services = fromLogs.Union(fromTraces).Union(fromMetrics).OrderBy(s => s).ToList();
            return new ServiceCatalog { From = Iso(from), To = Iso(to), Services = services };
        }

        public async Task<AlertRule[]> ListAlertRulesAsync(CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, RuleType, Metric, ServiceName, ThresholdValue, WindowMinutes, Enabled, CreatedAt FROM nafas_alert_rules ORDER BY CreatedAt DESC;";
            var rules = new List<AlertRule>();
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rules.Add(ReadAlertRule(reader));
            }
            return rules.ToArray();
        }

        public async Task<AlertRule> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO nafas_alert_rules (Name, RuleType, Metric, ServiceName, ThresholdValue, WindowMinutes, Enabled, CreatedAt)
VALUES (@name, @ruleType, @metric, @serviceName, @threshold, @window, 1, @createdAt);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("@name", request.Name);
            command.Parameters.AddWithValue("@ruleType", request.RuleType);
            command.Parameters.AddWithValue("@metric", (object?)request.Metric ?? DBNull.Value);
            command.Parameters.AddWithValue("@serviceName", (object?)request.ServiceName ?? DBNull.Value);
            command.Parameters.AddWithValue("@threshold", (object?)request.ThresholdValue ?? DBNull.Value);
            command.Parameters.AddWithValue("@window", request.WindowMinutes);
            command.Parameters.AddWithValue("@createdAt", Iso(DateTime.UtcNow));

            var id = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
            return (await ListAlertRulesAsync(ct).ConfigureAwait(false)).First(r => r.Id == id);
        }

        public async Task<AlertRule> UpdateAlertRuleAsync(long id, UpdateAlertRuleRequest request, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE nafas_alert_rules
SET Name = @name, Metric = @metric, ServiceName = @serviceName, ThresholdValue = @threshold, WindowMinutes = @window, Enabled = @enabled
WHERE Id = @id;";
            command.Parameters.AddWithValue("@name", request.Name);
            command.Parameters.AddWithValue("@metric", (object?)request.Metric ?? DBNull.Value);
            command.Parameters.AddWithValue("@serviceName", (object?)request.ServiceName ?? DBNull.Value);
            command.Parameters.AddWithValue("@threshold", (object?)request.ThresholdValue ?? DBNull.Value);
            command.Parameters.AddWithValue("@window", request.WindowMinutes);
            command.Parameters.AddWithValue("@enabled", request.Enabled ? 1 : 0);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            return (await ListAlertRulesAsync(ct).ConfigureAwait(false)).First(r => r.Id == id);
        }

        public async Task DeleteAlertRuleAsync(long id, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            // nafas_alert_incidents.RuleId REFERENCES nafas_alert_rules(Id)
            // with no ON DELETE CASCADE (SQLite enforces this by default in
            // recent Microsoft.Data.Sqlite versions) -- a rule that ever
            // fired (NafasAlertEvaluationHostedService.CreateIncidentAsync)
            // has incident rows pointing at it, so deleting the rule alone
            // throws a foreign-key-constraint error. Delete its incident
            // history first, in the same statement batch -- losing a
            // deleted rule's incident history along with it matches how
            // most alerting tools behave, and is far less surprising than
            // "can't delete a rule that has ever fired".
            command.CommandText = @"
DELETE FROM nafas_alert_incidents WHERE RuleId = @id;
DELETE FROM nafas_alert_rules WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        public async Task<AlertIncident[]> ListAlertIncidentsAsync(CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, RuleId, RuleName, RuleType, Status, Detail, TriggeredAt, ResolvedAt FROM nafas_alert_incidents ORDER BY TriggeredAt DESC LIMIT 200;";
            var incidents = new List<AlertIncident>();
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                incidents.Add(ReadAlertIncident(reader));
            }
            return incidents.ToArray();
        }

        public async Task<AlertIncident?> GetOpenIncidentForRuleAsync(long ruleId, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, RuleId, RuleName, RuleType, Status, Detail, TriggeredAt, ResolvedAt FROM nafas_alert_incidents WHERE RuleId = @ruleId AND Status = 'open' ORDER BY TriggeredAt DESC LIMIT 1;";
            command.Parameters.AddWithValue("@ruleId", ruleId);
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            return ReadAlertIncident(reader);
        }

        public async Task<AlertIncident> CreateIncidentAsync(long ruleId, string ruleName, string ruleType, string? detail, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO nafas_alert_incidents (RuleId, RuleName, RuleType, Status, Detail, TriggeredAt, ResolvedAt)
VALUES (@ruleId, @ruleName, @ruleType, 'open', @detail, @triggeredAt, NULL);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("@ruleId", ruleId);
            command.Parameters.AddWithValue("@ruleName", ruleName);
            command.Parameters.AddWithValue("@ruleType", ruleType);
            command.Parameters.AddWithValue("@detail", (object?)detail ?? DBNull.Value);
            command.Parameters.AddWithValue("@triggeredAt", Iso(DateTime.UtcNow));

            var id = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
            return new AlertIncident { Id = id, RuleId = ruleId, RuleName = ruleName, RuleType = ruleType, Status = "open", Detail = detail, TriggeredAt = Iso(DateTime.UtcNow) };
        }

        public async Task ResolveIncidentAsync(long incidentId, CancellationToken ct = default)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE nafas_alert_incidents SET Status = 'resolved', ResolvedAt = @resolvedAt WHERE Id = @id;";
            command.Parameters.AddWithValue("@resolvedAt", Iso(DateTime.UtcNow));
            command.Parameters.AddWithValue("@id", incidentId);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // ---- shared helpers ----

        private static AlertIncident ReadAlertIncident(SqliteDataReader reader) => new AlertIncident
        {
            Id = reader.GetInt64(0), RuleId = reader.GetInt64(1), RuleName = reader.GetString(2), RuleType = reader.GetString(3),
            Status = reader.GetString(4), Detail = reader.IsDBNull(5) ? null : reader.GetString(5),
            TriggeredAt = reader.GetString(6), ResolvedAt = reader.IsDBNull(7) ? null : reader.GetString(7),
        };

        private static AlertRule ReadAlertRule(SqliteDataReader reader) => new AlertRule
        {
            Id = reader.GetInt64(0), Name = reader.GetString(1), RuleType = reader.GetString(2),
            Metric = reader.IsDBNull(3) ? null : reader.GetString(3), ServiceName = reader.IsDBNull(4) ? null : reader.GetString(4),
            ThresholdValue = reader.IsDBNull(5) ? (double?)null : reader.GetDouble(5), WindowMinutes = reader.GetInt32(6),
            Enabled = reader.GetInt64(7) != 0, CreatedAt = reader.GetString(8),
        };

        private static string ServiceFilter(string? serviceName) => string.IsNullOrWhiteSpace(serviceName) ? "" : " AND ServiceName = @service";

        private static (DateTime from, DateTime to, string? service) Params(DateTime from, DateTime to, string? service) => (from, to, service);

        private static void AddParams(SqliteCommand command, (DateTime from, DateTime to, string? service) p)
        {
            command.Parameters.AddWithValue("@from", Iso(p.from));
            command.Parameters.AddWithValue("@to", Iso(p.to));
            if (!string.IsNullOrWhiteSpace(p.service)) command.Parameters.AddWithValue("@service", p.service);
        }

        private static void AddSearchParams(SqliteCommand command, DateTime from, DateTime to, string? serviceName, string? level, string? search)
        {
            AddParams(command, (from, to, serviceName));
            if (!string.IsNullOrWhiteSpace(level)) command.Parameters.AddWithValue("@level", level!.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(search)) command.Parameters.AddWithValue("@search", $"%{search}%");
        }

        private static void AddTraceSearchParams(SqliteCommand command, DateTime from, DateTime to, string? serviceName, string? traceId)
        {
            AddParams(command, (from, to, serviceName));
            if (!string.IsNullOrWhiteSpace(traceId)) command.Parameters.AddWithValue("@traceId", traceId);
        }

        private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, (DateTime from, DateTime to, string? service) p, CancellationToken ct)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql + ";";
            AddParams(command, p);
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
        }

        private static async Task<List<string>> DistinctServiceNamesAsync(SqliteConnection connection, string table, DateTime from, DateTime to, CancellationToken ct)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT DISTINCT ServiceName FROM {table} WHERE Timestamp >= @from AND Timestamp < @to;";
            AddParams(command, (from, to, null));
            var services = new List<string>();
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) services.Add(reader.GetString(0));
            return services;
        }

        private static async Task<double> AvgAsync(SqliteConnection connection, string metricName, DateTime from, DateTime to, string? serviceName, CancellationToken ct)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT AVG(Value) FROM nafas_metrics WHERE Timestamp >= @from AND Timestamp < @to AND MetricName = @metric" + ServiceFilter(serviceName) + ";";
            AddParams(command, (from, to, serviceName));
            command.Parameters.AddWithValue("@metric", metricName);
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is null or DBNull ? 0 : Convert.ToDouble(result);
        }

        private static async Task<(double current, double previous)> AvgCurrentAndPreviousAsync(SqliteConnection connection, string metricName, DateTime from, DateTime to, string? serviceName, CancellationToken ct)
        {
            var span = to - from;
            var current = await AvgAsync(connection, metricName, from, to, serviceName, ct).ConfigureAwait(false);
            var previous = await AvgAsync(connection, metricName, from - span, from, serviceName, ct).ConfigureAwait(false);
            return (current, previous);
        }

        private static async Task<(List<double> durations, long errorCount, long total)> TraceStatsAsync(SqliteConnection connection, DateTime from, DateTime to, string? serviceName, CancellationToken ct)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DurationNanos, StatusCode FROM nafas_traces WHERE Timestamp >= @from AND Timestamp < @to" + ServiceFilter(serviceName) + ";";
            AddParams(command, (from, to, serviceName));

            var durations = new List<double>();
            long errorCount = 0, total = 0;
            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                total++;
                // nanoseconds -> milliseconds: only used by GetTracesOverviewKpiAsync,
                // whose avgDuration/p95Duration go straight into traces.vue's
                // formatDuration(ms) with no client-side conversion (unlike
                // TraceSearch's Duration field, which traces.vue itself
                // divides by 1_000_000 -- that one stays raw nanoseconds).
                if (!reader.IsDBNull(0)) durations.Add(reader.GetInt64(0) / 1_000_000.0);
                if (!reader.IsDBNull(1) && string.Equals(reader.GetString(1), "ERROR", StringComparison.OrdinalIgnoreCase)) errorCount++;
            }
            return (durations, errorCount, total);
        }

        private static double PercentChange(double current, double previous)
        {
            if (previous == 0) return current == 0 ? 0 : 100;
            return (current - previous) / previous * 100;
        }

        private static double Percentile(List<double> values, double percentile)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, index))];
        }

        private sealed class Bucket { public DateTime BucketStart; public List<double> Values = new(); }

        private static List<Bucket> BucketTimestamps(List<DateTime> timestamps, DateTime from, DateTime to, int intervalMinutes) =>
            BucketValues(timestamps.Select(t => (t, 0d)).ToList(), from, to, intervalMinutes);

        private static List<Bucket> BucketValues(List<(DateTime Timestamp, double Value)> points, DateTime from, DateTime to, int intervalMinutes)
        {
            intervalMinutes = Math.Max(1, intervalMinutes);
            var interval = TimeSpan.FromMinutes(intervalMinutes);
            var buckets = new List<Bucket>();
            for (var start = from; start < to; start += interval)
            {
                buckets.Add(new Bucket { BucketStart = start });
            }
            if (buckets.Count == 0) buckets.Add(new Bucket { BucketStart = from });

            foreach (var (timestamp, value) in points)
            {
                var offsetMinutes = (timestamp - from).TotalMinutes;
                var index = (int)(offsetMinutes / intervalMinutes);
                if (index < 0) index = 0;
                if (index >= buckets.Count) index = buckets.Count - 1;
                buckets[index].Values.Add(value);
            }

            return buckets;
        }
    }
}
