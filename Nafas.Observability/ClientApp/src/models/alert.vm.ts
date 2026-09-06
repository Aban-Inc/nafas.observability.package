// Mirrors no.viewmodel.stack's Alert*ViewModel family -- Phase 0 of the
// alerting roadmap. Rule CRUD + incident listing only; no notification
// delivery yet (Phase 1+). No tenantId anywhere here -- unlike the SaaS
// platform this is derived from, this package has exactly one implicit
// "tenant" (the app it's embedded in).
export interface AlertRuleViewModel {
  id: number
  name: string
  ruleType: 'threshold' | 'absence'
  metric?: 'error_rate' | 'log_volume' | null
  serviceName?: string | null
  thresholdValue?: number | null
  windowMinutes: number
  enabled: boolean
  createdAt: string
}

// ruleType is fixed at creation -- see AlertViewModel.cs's own comment on
// why (threshold vs. absence are different evaluation paths, not an
// editable toggle).
export interface CreateAlertRuleRequest {
  name: string
  ruleType: 'threshold' | 'absence'
  metric?: 'error_rate' | 'log_volume' | null
  serviceName?: string | null
  thresholdValue?: number | null
  windowMinutes: number
}

export interface UpdateAlertRuleRequest {
  name: string
  metric?: 'error_rate' | 'log_volume' | null
  serviceName?: string | null
  thresholdValue?: number | null
  windowMinutes: number
  enabled: boolean
}

export interface AlertIncidentViewModel {
  id: number
  ruleId: number
  ruleName: string
  ruleType: 'threshold' | 'absence'
  status: 'open' | 'resolved'
  detail?: string | null
  triggeredAt: string
  resolvedAt?: string | null
}

// Delivery beyond the in-app bell is one developer-configured webhook
// (NafasServerOptions.AlertWebhookUrl, set in the consuming app's own
// Program.cs) -- not a runtime-managed, per-installation channel list, so
// there's no NotificationChannelViewModel/create/update/response family
// here. An earlier version of this file had one, carried over from the SaaS
// platform's own multi-tenant/multi-channel model; removed along with
// alerts.vue's Channels UI section since it never had a real backend and
// doesn't match this package's design.
