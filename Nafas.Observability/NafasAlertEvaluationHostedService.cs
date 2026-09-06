using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Storage;

namespace Nafas.Observability
{
    /// <summary>
    /// Turns a stored nafas_alert_rules row into an actual nafas_alert_incidents
    /// row -- the piece that was missing before this: rule CRUD
    /// (NafasDashboardEndpoints.cs's api/v1/rules routes) only ever stored
    /// rules, nothing evaluated them against real data. Runs on a fixed
    /// interval, not event-driven off ingestion -- alert rules operate over a
    /// rolling time window (WindowMinutes), so "did this window's condition
    /// hold" is naturally a poll, not a reaction to any single incoming
    /// record.
    ///
    /// Two rule types (see ClientApp/src/models/alert.vm.ts, the source of
    /// truth this mirrors):
    ///   - "threshold": Metric ("error_rate" | "log_volume") over the last
    ///     WindowMinutes, optionally scoped to ServiceName, breaches when it
    ///     exceeds ThresholdValue.
    ///   - "absence": breaches when ServiceName (or, if null, every service
    ///     combined) produced zero logs in the last WindowMinutes -- "this
    ///     went silent".
    /// Both are computed by reusing GetErrorRateKpiAsync/GetLogVolumeKpiAsync
    /// -- the exact same queries the dashboard's own KPI cards already run --
    /// rather than adding parallel query logic.
    ///
    /// State transitions only (open on breach, resolve on clear) -- a rule
    /// that stays breached across evaluations does NOT re-fire every
    /// interval; INafasQueryStore.GetOpenIncidentForRuleAsync is checked
    /// first specifically to prevent that. Firing both the live "alerts" SSE
    /// channel (NafasLiveFeed, same channel/shape NafasDashboardEndpoints.
    /// EmitDebugAlert used as a stand-in before this existed) and the
    /// developer-configured webhook (INafasAlertWebhookSender) exactly once
    /// per transition.
    /// </summary>
    internal sealed class NafasAlertEvaluationHostedService : BackgroundService
    {
        // Coarse on purpose -- rule windows are measured in minutes, so
        // nothing is lost by checking every 30s instead of continuously, and
        // it keeps this cheap (a handful of KPI-sized queries per rule) even
        // with several rules configured.
        private static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(30);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly INafasQueryStore _store;
        private readonly INafasAlertWebhookSender _webhookSender;
        private readonly NafasLiveFeed _liveFeed;
        private readonly ILogger<NafasAlertEvaluationHostedService> _logger;

        public NafasAlertEvaluationHostedService(INafasQueryStore store, INafasAlertWebhookSender webhookSender, NafasLiveFeed liveFeed, ILogger<NafasAlertEvaluationHostedService> logger)
        {
            _store = store;
            _webhookSender = webhookSender;
            _liveFeed = liveFeed;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateAllRulesAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // Same "never take down the consumer's app" posture as
                    // NafasRetentionHostedService's own sweep loop.
                    _logger.LogError(ex, "Nafas alert evaluation pass failed; will retry at the next interval.");
                }

                try
                {
                    await Task.Delay(EvaluationInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task EvaluateAllRulesAsync(CancellationToken ct)
        {
            var rules = await _store.ListAlertRulesAsync(ct).ConfigureAwait(false);
            foreach (var rule in rules)
            {
                if (!rule.Enabled) continue;

                try
                {
                    await EvaluateRuleAsync(rule, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // One bad rule (e.g. a metric name that somehow doesn't
                    // match anything) must never stop the rest from being
                    // evaluated this pass.
                    _logger.LogWarning(ex, "Nafas alert evaluation failed for rule {RuleId} ({RuleName}); will retry next interval.", rule.Id, rule.Name);
                }
            }
        }

        private async Task EvaluateRuleAsync(AlertRule rule, CancellationToken ct)
        {
            var to = DateTime.UtcNow;
            var from = to.AddMinutes(-Math.Max(1, rule.WindowMinutes));

            bool breached;
            string detail;

            if (string.Equals(rule.RuleType, "absence", StringComparison.OrdinalIgnoreCase))
            {
                var volume = await _store.GetLogVolumeKpiAsync(from, to, rule.ServiceName, ct).ConfigureAwait(false);
                breached = volume.LogCount == 0;
                detail = rule.ServiceName != null
                    ? $"No logs received from \"{rule.ServiceName}\" in the last {rule.WindowMinutes} minute(s)."
                    : $"No logs received from any service in the last {rule.WindowMinutes} minute(s).";
            }
            else // "threshold"
            {
                double value;
                string unit;

                if (string.Equals(rule.Metric, "error_rate", StringComparison.OrdinalIgnoreCase))
                {
                    var errors = await _store.GetErrorRateKpiAsync(from, to, rule.ServiceName, ct).ConfigureAwait(false);
                    var total = await _store.GetLogVolumeKpiAsync(from, to, rule.ServiceName, ct).ConfigureAwait(false);
                    value = total.LogCount > 0 ? errors.ErrorCount / (double)total.LogCount * 100.0 : 0.0;
                    unit = "%";
                }
                else // "log_volume"
                {
                    var volume = await _store.GetLogVolumeKpiAsync(from, to, rule.ServiceName, ct).ConfigureAwait(false);
                    value = volume.LogCount;
                    unit = "";
                }

                var threshold = rule.ThresholdValue ?? 0;
                breached = value > threshold;
                detail = $"{rule.Metric} is {value.ToString("0.##", CultureInfo.InvariantCulture)}{unit} over the last {rule.WindowMinutes} minute(s) (threshold: {threshold.ToString("0.##", CultureInfo.InvariantCulture)}{unit}).";
            }

            var openIncident = await _store.GetOpenIncidentForRuleAsync(rule.Id, ct).ConfigureAwait(false);

            if (breached && openIncident == null)
            {
                await _store.CreateIncidentAsync(rule.Id, rule.Name, rule.RuleType, detail, ct).ConfigureAwait(false);
                PublishLive("incident.open", rule.Id, rule.Name, detail);
                await _webhookSender.SendAsync(new NafasAlertWebhookPayload
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    RuleType = rule.RuleType,
                    Status = "open",
                    Detail = detail,
                    TriggeredAtUtc = DateTime.UtcNow,
                }, ct).ConfigureAwait(false);
            }
            else if (!breached && openIncident != null)
            {
                await _store.ResolveIncidentAsync(openIncident.Id, ct).ConfigureAwait(false);
                PublishLive("incident.resolved", rule.Id, rule.Name, detail);
                var triggeredAtUtc = DateTime.Parse(openIncident.TriggeredAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                await _webhookSender.SendAsync(new NafasAlertWebhookPayload
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    RuleType = rule.RuleType,
                    Status = "resolved",
                    Detail = detail,
                    TriggeredAtUtc = triggeredAtUtc,
                    ResolvedAtUtc = DateTime.UtcNow,
                }, ct).ConfigureAwait(false);
            }
            // else: state unchanged (still breached, or still fine) -- no
            // incident/webhook/live-event churn every 30s for a condition
            // that hasn't actually changed.
        }

        // Matches ClientApp/src/composables/useAlertStream.ts's
        // AlertStreamEvent shape exactly -- see NafasDashboardEndpoints.
        // EmitDebugAlert for the temporary stand-in this makes redundant.
        private void PublishLive(string type, long ruleId, string ruleName, string? detail)
        {
            var payload = new
            {
                type,
                ruleId,
                ruleName,
                detail,
                at = DateTime.UtcNow.ToString("O"),
            };
            _liveFeed.Publish("alerts", JsonSerializer.Serialize(payload, JsonOptions));
        }
    }
}
