<script setup lang="ts">
// Alerting roadmap: rule CRUD + incident visibility (Phase 0). Delivery
// beyond the in-app bell (NotificationBell.vue/AppTopBar.vue) is one
// developer-configured webhook (NafasServerOptions.AlertWebhookUrl,
// NafasAlertWebhookSender.cs) -- not a runtime-managed multi-channel UI, so
// there's no channel list/create/edit/delete on this page. An earlier
// version of this page had one, carried over from the SaaS platform's own
// multi-tenant/multi-channel model; removed outright since it never had a
// real backend behind it and doesn't match this package's single-webhook
// design (see NafasServerOptions.AlertWebhookUrl's own comment).
import { ref, reactive, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import { useApiError } from '@/composables/useApiError'
import AlertService from '@/services/alert.service'
import type {
  AlertIncidentViewModel,
  AlertRuleViewModel,
  CreateAlertRuleRequest
} from '@/models/alert.vm'

const { t } = useI18n()
const _alert_service = new AlertService()
const { notifyError } = useApiError()

const RULE_TYPE_OPTIONS = computed(() => [
  { label: t('alerts.ruleType.threshold'), value: 'threshold' },
  { label: t('alerts.ruleType.absence'), value: 'absence' },
])
const METRIC_OPTIONS = computed(() => [
  { label: t('alerts.metric.errorRate'), value: 'error_rate' },
  { label: t('alerts.metric.logVolume'), value: 'log_volume' },
])

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}

function ruleSummary(rule: AlertRuleViewModel): string {
  if (rule.ruleType === 'absence') {
    return t('alerts.summary.absence', { window: rule.windowMinutes, service: rule.serviceName ?? t('alerts.anyService') })
  }
  const metricLabel = rule.metric === 'error_rate' ? t('alerts.metric.errorRate') : t('alerts.metric.logVolume')
  const unit = rule.metric === 'error_rate' ? '%' : ''
  return t('alerts.summary.threshold', {
    metric: metricLabel,
    threshold: `${rule.thresholdValue}${unit}`,
    window: rule.windowMinutes,
    service: rule.serviceName ?? t('alerts.anyService')
  })
}

// ---- Rules ----
const rulesLoading = ref(true)
const rules = ref<AlertRuleViewModel[]>([])

async function loadRules() {
  rulesLoading.value = true
  try {
    rules.value = await _alert_service.ListRules()
  } catch (err) {
    console.error('Failed to load alert rules', err)
    notifyError(t('alerts.title'))
  } finally {
    rulesLoading.value = false
  }
}

// ---- Incidents ----
const incidentsLoading = ref(true)
const incidents = ref<AlertIncidentViewModel[]>([])

async function loadIncidents() {
  incidentsLoading.value = true
  try {
    incidents.value = await _alert_service.ListIncidents()
  } catch (err) {
    console.error('Failed to load incidents', err)
    notifyError(t('alerts.incidents.title'))
  } finally {
    incidentsLoading.value = false
  }
}

onMounted(() => {
  loadRules()
  loadIncidents()
})

// ---- Create ----
const showCreateForm = ref(false)
const creating = ref(false)
const createError = ref<string | null>(null)
const createForm = reactive({
  name: '',
  ruleType: 'threshold' as 'threshold' | 'absence',
  metric: 'error_rate' as 'error_rate' | 'log_volume',
  serviceName: '',
  thresholdValue: 5,
  windowMinutes: 5,
})

function openCreateForm() {
  createForm.name = ''
  createForm.ruleType = 'threshold'
  createForm.metric = 'error_rate'
  createForm.serviceName = ''
  createForm.thresholdValue = 5
  createForm.windowMinutes = 5
  createError.value = null
  showCreateForm.value = true
}

async function confirmCreate() {
  if (!createForm.name.trim()) return
  creating.value = true
  createError.value = null
  try {
    const request: CreateAlertRuleRequest = {
      name: createForm.name.trim(),
      ruleType: createForm.ruleType,
      metric: createForm.ruleType === 'threshold' ? createForm.metric : null,
      serviceName: createForm.serviceName.trim() || null,
      thresholdValue: createForm.ruleType === 'threshold' ? createForm.thresholdValue : null,
      windowMinutes: createForm.windowMinutes,
    }
    const created = await _alert_service.CreateRule(request)
    rules.value.unshift(created)
    showCreateForm.value = false
  } catch (err: any) {
    createError.value = err?.data?.detail ?? err?.data?.title ?? t('alerts.createError')
  } finally {
    creating.value = false
  }
}

// ---- Edit (enable/disable + tune threshold, inline) ----
const editingId = ref<number | null>(null)
const editing = ref(false)
const editError = ref<string | null>(null)
const editForm = reactive({
  name: '',
  metric: 'error_rate' as 'error_rate' | 'log_volume',
  serviceName: '',
  thresholdValue: 5,
  windowMinutes: 5,
  enabled: true,
})

function openEditForm(rule: AlertRuleViewModel) {
  editingId.value = rule.id
  editForm.name = rule.name
  editForm.metric = (rule.metric as 'error_rate' | 'log_volume') ?? 'error_rate'
  editForm.serviceName = rule.serviceName ?? ''
  editForm.thresholdValue = rule.thresholdValue ?? 5
  editForm.windowMinutes = rule.windowMinutes
  editForm.enabled = rule.enabled
  editError.value = null
}

function cancelEdit() {
  editingId.value = null
}

async function confirmEdit(rule: AlertRuleViewModel) {
  if (!editForm.name.trim()) return
  editing.value = true
  editError.value = null
  try {
    const updated = await _alert_service.UpdateRule(rule.id, {
      name: editForm.name.trim(),
      metric: rule.ruleType === 'threshold' ? editForm.metric : null,
      serviceName: editForm.serviceName.trim() || null,
      thresholdValue: rule.ruleType === 'threshold' ? editForm.thresholdValue : null,
      windowMinutes: editForm.windowMinutes,
      enabled: editForm.enabled,
    })
    const index = rules.value.findIndex(r => r.id === updated.id)
    if (index !== -1) rules.value[index] = updated
    editingId.value = null
  } catch (err: any) {
    editError.value = err?.data?.detail ?? err?.data?.title ?? t('alerts.updateError')
  } finally {
    editing.value = false
  }
}

// ---- Delete ----
const confirmingDeleteId = ref<number | null>(null)
const deleting = ref(false)

async function confirmDelete(id: number) {
  deleting.value = true
  try {
    await _alert_service.DeleteRule(id)
    rules.value = rules.value.filter(r => r.id !== id)
    confirmingDeleteId.value = null
  } catch (err) {
    console.error('Failed to delete alert rule', err)
  } finally {
    deleting.value = false
  }
}

</script>

<template>
  <div class="alerts-page">
    <div class="page-hdr">
      <div>
        <h1 class="page-title">{{ t('alerts.title') }}</h1>
        <p class="page-sub">{{ t('alerts.subtitle') }}</p>
      </div>
      <Button v-if="!showCreateForm" :label="t('alerts.newRule')" @click="openCreateForm" />
    </div>

    <div v-if="showCreateForm" class="rule-form">
        <div class="rule-form-grid">
          <div class="field">
            <label class="field-label">{{ t('alerts.form.name') }}</label>
            <InputText v-model="createForm.name" class="field-input" :placeholder="t('alerts.form.namePlaceholder')" />
          </div>
          <div class="field">
            <label class="field-label">{{ t('alerts.form.ruleType') }}</label>
            <Select v-model="createForm.ruleType" :options="RULE_TYPE_OPTIONS" option-label="label" option-value="value" class="field-input" />
          </div>
          <div class="field">
            <label class="field-label">{{ t('alerts.form.service') }}</label>
            <InputText v-model="createForm.serviceName" class="field-input" :placeholder="t('alerts.anyService')" />
          </div>
          <template v-if="createForm.ruleType === 'threshold'">
            <div class="field">
              <label class="field-label">{{ t('alerts.form.metric') }}</label>
              <Select v-model="createForm.metric" :options="METRIC_OPTIONS" option-label="label" option-value="value" class="field-input" />
            </div>
            <div class="field">
              <label class="field-label">{{ t('alerts.form.threshold') }}</label>
              <InputNumber v-model="createForm.thresholdValue" :min="0" class="field-input" />
            </div>
          </template>
          <div class="field">
            <label class="field-label">{{ t('alerts.form.window') }}</label>
            <InputNumber v-model="createForm.windowMinutes" :min="1" class="field-input" />
          </div>
        </div>
        <span v-if="createError" class="field-error">{{ createError }}</span>
        <div class="rule-form-actions">
          <Button :label="t('alerts.form.cancel')" severity="secondary" text @click="showCreateForm = false" />
          <Button :label="t('alerts.form.create')" :loading="creating" :disabled="!createForm.name.trim()" @click="confirmCreate" />
        </div>
      </div>

      <div class="alerts-section">
        <div class="section-title">{{ t('alerts.rulesTitle') }}</div>

        <div v-if="rulesLoading" class="alerts-state">{{ t('alerts.loading') }}</div>
        <div v-else-if="rules.length === 0" class="alerts-state">{{ t('alerts.noRules') }}</div>

        <div v-else class="rule-list">
          <div v-for="rule in rules" :key="rule.id" class="rule-row-wrap">
            <div class="rule-row">
              <div class="rule-row-main">
                <span class="rule-name">{{ rule.name }}</span>
                <span class="rule-summary">{{ ruleSummary(rule) }}</span>
              </div>
              <span class="badge" :class="rule.enabled ? 'badge-active' : 'badge-muted'">
                {{ rule.enabled ? t('alerts.enabled') : t('alerts.disabled') }}
              </span>
              <div class="rule-actions">
                <button type="button" class="link-btn" @click="editingId === rule.id ? cancelEdit() : openEditForm(rule)">
                  {{ editingId === rule.id ? t('alerts.form.cancel') : t('alerts.edit') }}
                </button>
                <button type="button" class="link-btn link-btn-danger" @click="confirmingDeleteId = confirmingDeleteId === rule.id ? null : rule.id">
                  {{ t('alerts.delete') }}
                </button>
              </div>
            </div>

            <div v-if="editingId === rule.id" class="rule-form rule-form-inline">
              <div class="rule-form-grid">
                <div class="field">
                  <label class="field-label">{{ t('alerts.form.name') }}</label>
                  <InputText v-model="editForm.name" class="field-input" />
                </div>
                <div class="field">
                  <label class="field-label">{{ t('alerts.form.service') }}</label>
                  <InputText v-model="editForm.serviceName" class="field-input" :placeholder="t('alerts.anyService')" />
                </div>
                <template v-if="rule.ruleType === 'threshold'">
                  <div class="field">
                    <label class="field-label">{{ t('alerts.form.metric') }}</label>
                    <Select v-model="editForm.metric" :options="METRIC_OPTIONS" option-label="label" option-value="value" class="field-input" />
                  </div>
                  <div class="field">
                    <label class="field-label">{{ t('alerts.form.threshold') }}</label>
                    <InputNumber v-model="editForm.thresholdValue" :min="0" class="field-input" />
                  </div>
                </template>
                <div class="field">
                  <label class="field-label">{{ t('alerts.form.window') }}</label>
                  <InputNumber v-model="editForm.windowMinutes" :min="1" class="field-input" />
                </div>
              </div>
              <label class="enabled-toggle">
                <input type="checkbox" v-model="editForm.enabled" />
                {{ t('alerts.form.enabledToggle') }}
              </label>
              <span v-if="editError" class="field-error">{{ editError }}</span>
              <div class="rule-form-actions">
                <Button :label="t('alerts.form.cancel')" severity="secondary" text @click="cancelEdit" />
                <Button :label="t('alerts.form.save')" :loading="editing" :disabled="!editForm.name.trim()" @click="confirmEdit(rule)" />
              </div>
            </div>

            <div v-if="confirmingDeleteId === rule.id" class="delete-confirm">
              <span>{{ t('alerts.deleteConfirm', { name: rule.name }) }}</span>
              <div class="rule-form-actions">
                <Button :label="t('alerts.form.cancel')" severity="secondary" text @click="confirmingDeleteId = null" />
                <Button :label="t('alerts.form.confirmDelete')" severity="danger" :loading="deleting" @click="confirmDelete(rule.id)" />
              </div>
            </div>
          </div>
        </div>
      </div>

      <div class="alerts-section">
        <div class="section-title">{{ t('alerts.incidents.title') }}</div>

        <div v-if="incidentsLoading" class="alerts-state">{{ t('alerts.loading') }}</div>
        <div v-else-if="incidents.length === 0" class="alerts-state">{{ t('alerts.incidents.none') }}</div>

        <div v-else class="incident-table-wrap">
          <table class="incident-table">
            <thead>
              <tr>
                <th>{{ t('alerts.incidents.rule') }}</th>
                <th>{{ t('alerts.incidents.status') }}</th>
                <th>{{ t('alerts.incidents.detail') }}</th>
                <th>{{ t('alerts.incidents.triggered') }}</th>
                <th>{{ t('alerts.incidents.resolved') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="incident in incidents" :key="incident.id">
                <td class="incident-rule">{{ incident.ruleName }}</td>
                <td>
                  <span class="badge" :class="incident.status === 'open' ? 'badge-danger' : 'badge-active'">
                    {{ incident.status === 'open' ? t('alerts.incidents.open') : t('alerts.incidents.resolvedStatus') }}
                  </span>
                </td>
                <td class="incident-muted">{{ incident.detail ?? '—' }}</td>
                <td class="incident-muted">{{ formatDateTime(incident.triggeredAt) }}</td>
                <td class="incident-muted">{{ incident.resolvedAt ? formatDateTime(incident.resolvedAt) : '—' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

  </div>
</template>

<style scoped>
.alerts-page { display: flex; flex-direction: column; gap: 20px; }

.alerts-state { font-size: 13.5px; color: var(--text-color-secondary, #6B7280); padding: 16px 0; }

.alerts-section { display: flex; flex-direction: column; gap: 10px; }
.section-title { font-size: 13px; font-weight: 700; }

.rule-form, .delete-confirm {
  padding: 16px; border: 1px solid var(--surface-border, #E5E7EB); border-radius: 12px;
  display: flex; flex-direction: column; gap: 12px;
}
.rule-form-inline { border-radius: 8px; margin-top: 8px; }
.delete-confirm { flex-direction: row; align-items: center; justify-content: space-between; font-size: 13px; }

.rule-form-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 12px 14px; }
.field { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.field-label { font-size: 11.5px; font-weight: 500; color: var(--text-color-secondary, #6B7280); }
.field-input { width: 100%; }
.field-error { font-size: 12px; color: #E5484D; }

.enabled-toggle { display: flex; align-items: center; gap: 8px; font-size: 12.5px; color: var(--text-color-secondary, #6B7280); }

.rule-form-actions { display: flex; justify-content: flex-end; gap: 8px; }

.rule-list { display: flex; flex-direction: column; gap: 8px; }
.rule-row-wrap { border: 1px solid var(--surface-border, #E5E7EB); border-radius: 12px; padding: 12px 16px; }
.rule-row { display: flex; align-items: center; gap: 14px; }
.rule-row-main { display: flex; flex-direction: column; gap: 2px; flex: 1; min-width: 0; }
.rule-name { font-size: 13.5px; font-weight: 600; }
.rule-summary { font-size: 12px; color: var(--text-color-secondary, #6B7280); }
.rule-actions { display: flex; gap: 12px; flex-shrink: 0; }

.link-btn { background: none; border: none; padding: 0; font-size: 12.5px; font-weight: 500; color: var(--primary-color, #F4A62A); cursor: pointer; }
.link-btn-danger { color: #E5484D; }

.badge { display: inline-block; font-size: 10.5px; font-weight: 600; padding: 2px 8px; border-radius: 6px; flex-shrink: 0; }
.badge-active { color: #1F8A5F; background: rgba(31, 138, 95, 0.12); }
.badge-muted { color: #6B7280; background: rgba(107, 114, 128, 0.12); }
.badge-danger { color: #E5484D; background: rgba(229, 72, 77, 0.12); }

.incident-table-wrap { overflow-x: auto; border: 1px solid var(--surface-border, #E5E7EB); border-radius: 12px; }
.incident-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.incident-table th {
  text-align: start; padding: 10px 14px; font-size: 11px; font-weight: 600;
  letter-spacing: 0.3px; text-transform: uppercase; color: var(--text-color-secondary, #6B7280);
  border-bottom: 1px solid var(--surface-border, #E5E7EB); white-space: nowrap;
}
.incident-table td { padding: 10px 14px; border-bottom: 1px solid var(--surface-border, #E5E7EB); white-space: nowrap; }
.incident-table tr:last-child td { border-bottom: none; }
.incident-rule { font-weight: 600; }
.incident-muted { color: var(--text-color-secondary, #6B7280); }
</style>
