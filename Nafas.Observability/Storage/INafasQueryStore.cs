using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nafas.Observability.Storage
{
    /// <summary>
    /// The dashboard's entire read (and alert-rule write) surface against
    /// nafas_logs/nafas_metrics/nafas_traces/nafas_alert_*. One implementation
    /// per <see cref="DatabaseProvider"/>, same pattern as
    /// <see cref="INafasSchemaProvisioner"/> and for the same reason (genuine
    /// runtime polymorphism, not the SaaS platform repo's fixed-provider
    /// case).
    /// </summary>
    public interface INafasQueryStore
    {
        Task<ErrorRateKpi> GetErrorRateKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default);
        Task<LogVolumeKpi> GetLogVolumeKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default);
        Task<VolumeOverTime> GetVolumeOverTimeAsync(DateTime from, DateTime to, int intervalMinutes, string? serviceName, CancellationToken ct = default);
        Task<ErrorRateByService> GetErrorRateByServiceAsync(DateTime from, DateTime to, CancellationToken ct = default);
        Task<LogSearch> SearchLogsAsync(DateTime from, DateTime to, string? level, string? serviceName, string? search, int page, int pageSize, CancellationToken ct = default);

        Task<MetricEndpointsKpi> GetMetricEndpointsKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default);
        Task<MetricsOverviewKpi> GetMetricsOverviewKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default);
        Task<MetricTrend> GetMetricTrendAsync(DateTime from, DateTime to, int intervalMinutes, string metricName, string? serviceName, CancellationToken ct = default);
        Task<ServiceOverview> GetServiceOverviewAsync(DateTime from, DateTime to, CancellationToken ct = default);

        Task<ActiveTracesKpi> GetActiveTracesKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default);
        Task<TracesOverviewKpi> GetTracesOverviewKpiAsync(DateTime from, DateTime to, string? serviceName, CancellationToken ct = default);
        Task<LatencyHeatmap> GetLatencyHeatmapAsync(DateTime from, DateTime to, int bucketHours, string? serviceName, CancellationToken ct = default);
        Task<TraceSearch> SearchTracesAsync(DateTime from, DateTime to, string? serviceName, string? traceId, int page, int pageSize, CancellationToken ct = default);

        Task<ServiceCatalog> ListServicesAsync(DateTime from, DateTime to, CancellationToken ct = default);

        Task<AlertRule[]> ListAlertRulesAsync(CancellationToken ct = default);
        Task<AlertRule> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default);
        Task<AlertRule> UpdateAlertRuleAsync(long id, UpdateAlertRuleRequest request, CancellationToken ct = default);
        Task DeleteAlertRuleAsync(long id, CancellationToken ct = default);
        Task<AlertIncident[]> ListAlertIncidentsAsync(CancellationToken ct = default);

        // ---- incident state, used only by NafasAlertEvaluationHostedService
        // (the dashboard's own rule CRUD/incident-listing above never needs
        // these) ----

        /// <summary>
        /// The currently-open (Status = "open") incident for <paramref name="ruleId"/>,
        /// if any -- at most one can exist per rule at a time (the evaluator
        /// checks this before creating a new one, see its own comment).
        /// </summary>
        Task<AlertIncident?> GetOpenIncidentForRuleAsync(long ruleId, CancellationToken ct = default);

        Task<AlertIncident> CreateIncidentAsync(long ruleId, string ruleName, string ruleType, string? detail, CancellationToken ct = default);

        Task ResolveIncidentAsync(long incidentId, CancellationToken ct = default);
    }
}
