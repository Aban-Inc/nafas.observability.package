using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Nafas.Observability
{
    /// <summary>
    /// The incident payload POSTed to <see cref="NafasServerOptions.AlertWebhookUrl"/>.
    /// Deliberately not the full AlertIncidentViewModel shape the dashboard UI
    /// uses internally -- this is the package's own public contract with
    /// whatever system the consumer points the webhook at, and should stay
    /// stable even if the dashboard's internal model changes shape later.
    /// </summary>
    public sealed class NafasAlertWebhookPayload
    {
        public long RuleId { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string RuleType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "open" | "resolved"
        public string? Detail { get; set; }
        public DateTime TriggeredAtUtc { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
    }

    /// <summary>
    /// POSTs an alert incident to <see cref="NafasServerOptions.AlertWebhookUrl"/>
    /// as JSON -- this package never sends email/SMS/Slack itself (see
    /// NafasServerOptions.AlertWebhookUrl's own comment); the consuming app's
    /// own system takes it from there. Registered whether or not a webhook URL
    /// is actually configured -- <see cref="SendAsync"/> just no-ops (logging
    /// once) when it isn't, so callers (the not-yet-built alert evaluation
    /// engine) never need to branch on whether alerting is configured.
    /// </summary>
    public interface INafasAlertWebhookSender
    {
        Task SendAsync(NafasAlertWebhookPayload payload, CancellationToken cancellationToken = default);
    }

    internal sealed class NafasAlertWebhookSender : INafasAlertWebhookSender
    {
        private readonly NafasServerOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<NafasAlertWebhookSender> _logger;

        public NafasAlertWebhookSender(NafasServerOptions options, HttpClient httpClient, ILogger<NafasAlertWebhookSender> logger)
        {
            _options = options;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task SendAsync(NafasAlertWebhookPayload payload, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.AlertWebhookUrl))
            {
                _logger.LogDebug("Nafas alert webhook not configured (NafasServerOptions.AlertWebhookUrl is empty) -- incident for rule {RuleName} was not delivered anywhere.", payload.RuleName);
                return;
            }

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var response = await _httpClient.PostAsync(_options.AlertWebhookUrl, content, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Nafas alert webhook POST to {WebhookUrl} returned {StatusCode} for rule {RuleName}.", _options.AlertWebhookUrl, (int)response.StatusCode, payload.RuleName);
                }
            }
            catch (Exception ex)
            {
                // A failed webhook delivery never propagates -- the alert
                // still exists in the dashboard's own incident list either
                // way; this is a best-effort side channel, not the source of
                // truth.
                _logger.LogError(ex, "Nafas alert webhook POST to {WebhookUrl} failed for rule {RuleName}.", _options.AlertWebhookUrl, payload.RuleName);
            }
        }
    }
}
