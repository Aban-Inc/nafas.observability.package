<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import Skeleton from 'primevue/skeleton'
import Select from 'primevue/select'
import { useLogStore } from '@/stores/log.store'
import { useTimeRangeStore } from '@/stores/timeRange.store'
import LogService from '@/services/log.service'
import MetricService from '@/services/metric.service'
import TraceService from '@/services/trace.service'
import ServiceCatalogService from '@/services/service-catalog.service'
import { useApiError } from '@/composables/useApiError'
import { useLogStream } from '@/composables/useLogStream'
import LogVolumeChart from '@/components/LogVolumeChart.vue'
import ErrorRateChart from '@/components/ErrorRateChart.vue'
import TraceLatencyHeatmap from '@/components/TraceLatencyHeatmap.vue'
import RefreshButton from '@/components/RefreshButton.vue'
import type { LogEntryViewModel, LatencyHeatmapPointViewModel } from '@/models/dashboard.vm'

const { t } = useI18n()
const _log_store = useLogStore()
const _time_range_store = useTimeRangeStore()
const _log_service: LogService = new LogService();
const _metric_service: MetricService = new MetricService();
const _trace_service: TraceService = new TraceService();
const _service_catalog_service: ServiceCatalogService = new ServiceCatalogService();
const { notifyError } = useApiError()

// Governs every widget below except Recent Critical Logs, which deliberately
// stays a fixed "last 1 hour" regardless of this -- it's a live-tail panel
// (SSE-backed), not a historical KPI, so "recent" from the last year
// wouldn't mean anything.
const rangeHours = computed(() => _time_range_store.getHours)

// Chart bucket sizing scales with the selected range so "Last 365 Days"
// doesn't try to request/render 17,520 thirty-minute buckets (or an
// 8,760-column heatmap). Coarser buckets kick in as the range grows;
// unchanged (30m / hourly) at the original 24h default.
const volumeOverTimeIntervalMinutes = computed(() => {
  const h = rangeHours.value
  if (h <= 24) return 30
  if (h <= 24 * 7) return 240 // 4h
  if (h <= 24 * 30) return 720 // 12h
  return 1440 // daily
})

const heatmapBucketHours = computed(() => {
  const h = rangeHours.value
  if (h <= 24) return 1
  if (h <= 24 * 7) return 4
  if (h <= 24 * 30) return 12
  return 24 // daily
})

const volumeOverTimeBucketLabel = computed(() => {
  const m = volumeOverTimeIntervalMinutes.value
  if (m < 60) return t('dashboard.charts.bucketLabelMinutes', { m })
  return m < 1440 ? t('dashboard.charts.bucketLabelHours', { h: m / 60 }) : t('dashboard.charts.bucketLabelDaily')
})

const heatmapBucketLabel = computed(() => {
  const h = heatmapBucketHours.value
  return h === 1 ? t('dashboard.charts.heatmapHourly') : h < 24 ? t('dashboard.charts.bucketLabelHours', { h }) : t('dashboard.charts.heatmapDaily')
})

// Both the service filter and the auto-refresh interval are remembered
// across visits (localStorage, same guarded-access pattern as
// stores/layout.store.ts's theme/direction persistence) so a user doesn't
// have to re-pick them on every page load.
const STORAGE_KEY_SERVICE = 'nafas-dashboard-service'
const STORAGE_KEY_REFRESH_INTERVAL = 'nafas-dashboard-refresh-interval'

function readStoredString(key: string): string {
  return (typeof localStorage !== 'undefined' && localStorage.getItem(key)) || ''
}

function writeStoredString(key: string, value: string) {
  if (typeof localStorage === 'undefined') return
  localStorage.setItem(key, value)
}

// A distinct sentinel, not "" -- PrimeVue's Select treats an empty-string
// model value as "nothing selected" (renders blank instead of the matching
// option's label, regardless of an option actually having value: ''), so ""
// can never be the "All Services" value here. effectiveService() below
// converts this sentinel back to undefined ("no filter") wherever it's
// passed to an API call.
const ALL_SERVICES = '__all__'
const selectedService = ref<string>(readStoredString(STORAGE_KEY_SERVICE) || ALL_SERVICES)
const availableServices = ref<string[]>([])
const serviceOptions = computed(() => [
  { label: t('dashboard.serviceFilter.all'), value: ALL_SERVICES },
  ...availableServices.value.map(s => ({ label: s, value: s })),
])
const effectiveService = (): string | undefined => selectedService.value === ALL_SERVICES ? undefined : selectedService.value

// Auto-refresh: bounded to a fixed set of intervals, never finer than the
// BFF's own cache TTLs (30s for KPIs, 60s for charts) -- picking a shorter
// interval wouldn't get you fresher data, just wasted round trips. "Off" is
// the fallback on a first-ever visit (nothing in localStorage yet) so
// opening the dashboard never silently opts a new user into polling; once
// a choice is made, it's remembered from then on.
const REFRESH_OPTIONS = computed(() => [
  { label: t('dashboard.autoRefresh.off'), value: 0 },
  { label: t('dashboard.autoRefresh.every3m'), value: 180 },
  { label: t('dashboard.autoRefresh.every5m'), value: 300 },
  { label: t('dashboard.autoRefresh.every7m'), value: 420 },
  { label: t('dashboard.autoRefresh.every10m'), value: 600 },
])
const refreshInterval = ref<number>(Number(readStoredString(STORAGE_KEY_REFRESH_INTERVAL)) || 0)
let refreshTimerId: ReturnType<typeof setInterval> | undefined

const logVolumeLoading = ref(false)
const logVolumeError = ref(false)

const errorCount = ref<number | null>(null)
const errorRateLoading = ref(false)
const errorRateError = ref(false)

const volumeOverTime = ref<{ timestamp: string; count: number }[] | null>(null)
const volumeOverTimeLoading = ref(false)
const volumeOverTimeError = ref(false)

const errorRateByService = ref<{ service: string; errorRate: number }[] | null>(null)
const errorRateByServiceLoading = ref(false)
const errorRateByServiceError = ref(false)

const activeTraces = ref<{ count: number; percentageChange: number; trend: number[] } | null>(null)
const activeTracesLoading = ref(false)
const activeTracesError = ref(false)

const metricEndpoints = ref<{ healthy: number; total: number; scraping: number; trend: number[] } | null>(null)
const metricEndpointsLoading = ref(false)
const metricEndpointsError = ref(false)

const latencyHeatmap = ref<LatencyHeatmapPointViewModel[] | null>(null)
const latencyHeatmapLoading = ref(false)
const latencyHeatmapError = ref(false)

const recentCriticalLogsLoading = ref(false)

const lvl: Record<string, { color: string; bg: string }> = {
  ERROR: { color: '#FF8088', bg: 'rgba(240,97,109,0.14)' },
  WARN: { color: '#F2C94C', bg: 'rgba(242,201,76,0.14)' },
  INFO: { color: '#2DD4BF', bg: 'rgba(0,191,165,0.14)' },
  DEBUG: { color: '#7A808C', bg: 'rgba(122,128,140,0.14)' },
}
const recentCriticalLogs = ref<{ id: number; time: string; level: string; color?: string; bg?: string; service: string; msg: string }[]>([])

function getlogVolumeKPI() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const kpiFrom = encodeURIComponent(rangeStart.toISOString());
  const kpiTo = encodeURIComponent(now.toISOString());
  logVolumeLoading.value = true;
  logVolumeError.value = false;
  return _log_service.GetLogVolumeKPI(kpiFrom, kpiTo, effectiveService()).then(response => {
    _log_store.setCount(response?.logCount ?? 0);
  }).catch(err => {
    console.error('Failed to load log volume KPI', err);
    logVolumeError.value = true;
    notifyError('Total Logs');
  }).finally(() => {
    logVolumeLoading.value = false;
  });
}

function getErrorRateKPI() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const kpiFrom = encodeURIComponent(rangeStart.toISOString());
  const kpiTo = encodeURIComponent(now.toISOString());
  errorRateLoading.value = true;
  errorRateError.value = false;
  return _log_service.GetErrorRateKPI(kpiFrom, kpiTo, effectiveService()).then(response => {
    errorCount.value = response?.errorCount ?? null;
  }).catch(err => {
    console.error('Failed to load error rate KPI', err);
    errorRateError.value = true;
    notifyError('Error Rate');
  }).finally(() => {
    errorRateLoading.value = false;
  });
}

function getVolumeOverTime() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const chartFrom = encodeURIComponent(rangeStart.toISOString());
  const chartTo = encodeURIComponent(now.toISOString());
  volumeOverTimeLoading.value = true;
  volumeOverTimeError.value = false;
  return _log_service.GetVolumeOverTime(chartFrom, chartTo, volumeOverTimeIntervalMinutes.value, effectiveService()).then(response => {
    volumeOverTime.value = response?.data ?? null;
  }).catch(err => {
    console.error('Failed to load log volume over time', err);
    volumeOverTime.value = null;
    volumeOverTimeError.value = true;
    notifyError('Log Volume Over Time');
  }).finally(() => {
    volumeOverTimeLoading.value = false;
  });
}

function getErrorRateByServiceKPI() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const kpiFrom = encodeURIComponent(rangeStart.toISOString());
  const kpiTo = encodeURIComponent(now.toISOString());
  errorRateByServiceLoading.value = true;
  errorRateByServiceError.value = false;
  return _log_service.GetErrorRateByService(kpiFrom, kpiTo).then(response => {
    errorRateByService.value = response?.data
      ? response.data.map(s => ({ service: s.serviceName, errorRate: s.totalCount > 0 ? (s.errorCount / s.totalCount) * 100 : 0 }))
      : null;
  }).catch(err => {
    console.error('Failed to load error rate by service', err);
    errorRateByService.value = null;
    errorRateByServiceError.value = true;
    notifyError('Top Services by Error Rate');
  }).finally(() => {
    errorRateByServiceLoading.value = false;
  });
}

function getMetricEndpointsKPI() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const kpiFrom = encodeURIComponent(rangeStart.toISOString());
  const kpiTo = encodeURIComponent(now.toISOString());
  metricEndpointsLoading.value = true;
  metricEndpointsError.value = false;
  return _metric_service.GetMetricEndpointsKPI(kpiFrom, kpiTo, effectiveService()).then(response => {
    metricEndpoints.value = response
      ? { healthy: response.healthy, total: response.total, scraping: response.scraping, trend: response.trend }
      : null;
  }).catch(err => {
    console.error('Failed to load metric endpoints KPI', err);
    metricEndpointsError.value = true;
    notifyError('Metric Endpoints');
  }).finally(() => {
    metricEndpointsLoading.value = false;
  });
}

function getActiveTracesKPI() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const kpiFrom = encodeURIComponent(rangeStart.toISOString());
  const kpiTo = encodeURIComponent(now.toISOString());
  activeTracesLoading.value = true;
  activeTracesError.value = false;
  return _trace_service.GetActiveTracesKPI(kpiFrom, kpiTo, effectiveService()).then(response => {
    activeTraces.value = response
      ? { count: response.count, percentageChange: response.percentageChange, trend: response.trend }
      : null;
  }).catch(err => {
    console.error('Failed to load active traces KPI', err);
    activeTracesError.value = true;
    notifyError('Active Traces');
  }).finally(() => {
    activeTracesLoading.value = false;
  });
}

function getLatencyHeatmap() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const chartFrom = encodeURIComponent(rangeStart.toISOString());
  const chartTo = encodeURIComponent(now.toISOString());
  latencyHeatmapLoading.value = true;
  latencyHeatmapError.value = false;
  return _trace_service.GetLatencyHeatmap(chartFrom, chartTo, heatmapBucketHours.value, effectiveService()).then(response => {
    latencyHeatmap.value = response?.data ?? null;
  }).catch(err => {
    console.error('Failed to load trace latency heatmap', err);
    latencyHeatmap.value = null;
    latencyHeatmapError.value = true;
    notifyError('Trace Latency Heatmap');
  }).finally(() => {
    latencyHeatmapLoading.value = false;
  });
}

// SearchLogs only accepts a single 'level' value, so ERROR and WARN are fetched
// separately and merged, newest first, to get the 8 most recent critical logs.
function getRecentCriticalLogs() {
  const now = new Date();
  const oneHourAgo = new Date(now.getTime() - 60 * 60 * 1000)
  const kpiFrom = encodeURIComponent(oneHourAgo.toISOString());
  const kpiTo = encodeURIComponent(now.toISOString());
  recentCriticalLogsLoading.value = true;
  return Promise.all([
    _log_service.SearchLogs(kpiFrom, kpiTo, 'ERROR', undefined, undefined, 1, 8),
    _log_service.SearchLogs(kpiFrom, kpiTo, 'WARN', undefined, undefined, 1, 8),
  ]).then(([errors, warnings]) => {
    recentCriticalLogs.value = [...(errors?.data ?? []), ...(warnings?.data ?? [])]
      .sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime())
      .slice(0, 8)
      .map((l, idx) => ({
        id: idx,
        time: l.timestamp,
        level: l.severityText.toUpperCase(),
        color: lvl[l.severityText.toUpperCase()]?.color,
        bg: lvl[l.severityText.toUpperCase()]?.bg,
        service: l.serviceName,
        msg: l.body,
      }));
  }).catch(err => {
    console.error('Failed to load recent critical logs', err);
    recentCriticalLogs.value = [];
    notifyError('Recent Critical Logs');
  }).finally(() => {
    recentCriticalLogsLoading.value = false;
  });
}

// Live tail via SSE (ADR-010) — picks up where the initial getRecentCriticalLogs()
// backfill leaves off. SSE only pushes new events going forward (Vector's
// consumer group uses auto_offset_reset: latest, no historical replay), so the
// polling-based backfill above is still needed for "the last hour" on page load;
// this only handles what arrives after that.
// IDs start well above the initial batch's 0-7 range so :key stays unique
// without needing to touch getRecentCriticalLogs()'s own id assignment.
let liveLogIdCounter = 1000
function pushLiveLog(entry: LogEntryViewModel) {
  const severity = entry.severityText.toUpperCase()
  // The stream itself isn't filtered by severity (BFF filters per-tenant, not
  // per-severity, per ADR-010) — this panel only ever showed ERROR/WARN, so
  // filter here, same as the polling version did via SearchLogs(..., 'ERROR'/'WARN', ...).
  if (severity !== 'ERROR' && severity !== 'WARN') return

  recentCriticalLogs.value = [
    {
      id: liveLogIdCounter++,
      time: entry.timestamp,
      level: severity,
      color: lvl[severity]?.color,
      bg: lvl[severity]?.bg,
      service: entry.serviceName,
      msg: entry.body,
    },
    ...recentCriticalLogs.value,
  ].slice(0, 8) // always capped at 8, oldest dropped first
}

const { connect: connectLogStream, connected: logStreamConnected } = useLogStream(pushLiveLog)

// Scoped to the same selected range as everything else -- see
// ServiceCatalogRepository for why this reads distinct ServiceName values
// straight out of ClickHouse instead of the service registry: a registered
// service with no data yet wouldn't be worth filtering by, and this is
// guaranteed to match what agents actually send.
function getServiceList() {
  const now = new Date();
  const rangeStart = new Date(now.getTime() - rangeHours.value * 60 * 60 * 1000)
  const from = encodeURIComponent(rangeStart.toISOString());
  const to = encodeURIComponent(now.toISOString());
  return _service_catalog_service.ListServices(from, to).then(response => {
    availableServices.value = response?.services ?? [];
  }).catch(err => {
    console.error('Failed to load service list', err);
    availableServices.value = [];
  });
}

// Every KPI/chart on this dashboard except Recent Critical Logs (SSE-driven
// already, see pushLiveLog above) -- called on mount, on service-filter
// change, and by the auto-refresh timer below.
function refetchDashboard() {
  getlogVolumeKPI();
  getErrorRateKPI();
  getVolumeOverTime();
  getErrorRateByServiceKPI();
  getMetricEndpointsKPI();
  getActiveTracesKPI();
  getLatencyHeatmap();
}

function stopAutoRefresh() {
  if (refreshTimerId !== undefined) {
    clearInterval(refreshTimerId);
    refreshTimerId = undefined;
  }
}

function startAutoRefresh() {
  stopAutoRefresh();
  if (refreshInterval.value <= 0) return;
  refreshTimerId = setInterval(refetchDashboard, refreshInterval.value * 1000);
}

// Selecting an interval persists it and starts polling immediately; picking
// "Off" stops it.
watch(refreshInterval, (value) => {
  writeStoredString(STORAGE_KEY_REFRESH_INTERVAL, String(value));
  if (document.hidden) return; // visibilitychange handler below will start it once the tab is visible again
  startAutoRefresh();
});

// Switching the service filter persists it and re-fetches everything under
// the new scope.
watch(selectedService, (value) => {
  writeStoredString(STORAGE_KEY_SERVICE, value);
  refetchDashboard();
});

// Switching the topbar time range (persisted by timeRange.store.ts itself)
// re-fetches everything and refreshes the service list, since a wider/
// narrower window can change which services actually have data in it.
watch(() => _time_range_store.getRange, () => {
  getServiceList();
  refetchDashboard();
});

// Backgrounded tabs don't poll -- no point spending a ClickHouse/Redis round
// trip refreshing a dashboard nobody is looking at.
function handleVisibilityChange() {
  if (document.hidden) {
    stopAutoRefresh();
  } else if (refreshInterval.value > 0) {
    startAutoRefresh();
  }
}

onMounted(() => {
  getServiceList();
  refetchDashboard();
  getRecentCriticalLogs().finally(() => connectLogStream());
  // No manual disconnect() call here — useLogStream() already closes the
  // connection onUnmounted; a second call here would risk double-closing it.
  document.addEventListener('visibilitychange', handleVisibilityChange);
  // A persisted interval from a previous visit needs to be applied here --
  // the watch() below only fires on a *change*, not on the initial value
  // restored from localStorage, so without this a remembered "Every 5m"
  // would sit in the Select showing "5m" but never actually start polling.
  if (refreshInterval.value > 0 && !document.hidden) {
    startAutoRefresh();
  }
})

onUnmounted(() => {
  stopAutoRefresh();
  document.removeEventListener('visibilitychange', handleVisibilityChange);
})

// Sparklines — only drawn when the BFF actually returned a trend series.
// `|| 1` on the x-divisor guards a single-point series: without it,
// i / (arr.length - 1) is 0/0 = NaN, producing an invalid "MNaN ..." path
// (currently unreachable since the BFF always pads trend to a fixed 16
// buckets, but kept in sync with the same fix in metrics.vue's sparkLine).
const spark = (arr: number[]) => {
  const mx = Math.max(...arr), mn = Math.min(...arr)
  const xDivisor = (arr.length - 1) || 1
  return arr.map((v, i) =>
    (i ? 'L' : 'M') + (i / xDivisor * 100).toFixed(1) + ' ' + (31 - (v - mn) / ((mx - mn) || 1) * 28).toFixed(1)
  ).join(' ')
}
const spark2 = computed(() => activeTraces.value?.trend?.length ? spark(activeTraces.value.trend) : null)
const spark3 = computed(() => metricEndpoints.value?.trend?.length ? spark(metricEndpoints.value.trend) : null)

// Formatted log volume from BFF
const formattedLogVolume = computed(() => {
  const n = _log_store.getCount;
  if (n >= 1_000_000) return { value: (n / 1_000_000).toFixed(2), unit: 'M' }
  if (n >= 1_000) return { value: (n / 1_000).toFixed(1), unit: 'K' }
  return { value: String(n), unit: '' }
})

// Formatted error rate from BFF
const formattedErrorRate = computed(() => {
  if (errorCount.value === null || !_log_store.getCount) return null
  return ((errorCount.value / _log_store.getCount) * 100).toFixed(2)
})

// Active traces trend from BFF
const activeTracesTrend = computed(() => {
  if (activeTraces.value === null) return null
  const pct = activeTraces.value.percentageChange
  return { direction: pct >= 0 ? 'up' : 'down', label: `${pct >= 0 ? '↑' : '↓'} ${Math.abs(pct).toFixed(1)}%` }
})
</script>

<template>
<div class="dashboard-page">
  <div class="glow-orb" />

  <!-- Page header -->
  <div class="page-hdr">
    <div>
      <h1 class="page-title">{{ t('dashboard.title') }}</h1>
    </div>
    <div class="hdr-actions">
      <div class="hdr-select-group">
        <label class="hdr-select-label" for="dashboard-service-filter">{{ t('dashboard.serviceFilter.label') }}</label>
        <Select
          input-id="dashboard-service-filter"
          v-model="selectedService"
          :options="serviceOptions"
          option-label="label"
          option-value="value"
          class="hdr-select"
        />
      </div>
      <div class="hdr-select-group">
        <label class="hdr-select-label" for="dashboard-refresh-interval">{{ t('dashboard.autoRefresh.label') }}</label>
        <Select
          input-id="dashboard-refresh-interval"
          v-model="refreshInterval"
          :options="REFRESH_OPTIONS"
          option-label="label"
          option-value="value"
          class="hdr-select"
        >
          <template #value="{ value }">
            <span class="dot-green" v-if="value > 0" /><span>{{ REFRESH_OPTIONS.find(o => o.value === value)?.label }}</span>
          </template>
        </Select>
      </div>
    </div>
  </div>

  <!-- KPI row -->
  <div class="kpi-grid">
    <!-- Total Logs -->
    <div class="card">
      <div class="card-top">
        <span class="card-label">{{ t('dashboard.kpi.totalLogs') }} · {{ _time_range_store.getLabel }}</span>
        <div class="card-top-actions">
          <RefreshButton :loading="logVolumeLoading" @refresh="getlogVolumeKPI" />
          <div class="card-icon teal">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none">
              <path d="M4 6h16M4 12h16M4 18h10" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" />
            </svg>
          </div>
        </div>
      </div>
      <Skeleton v-if="logVolumeLoading" width="90px" height="30px" />
      <div v-else class="card-value">
        <template v-if="!logVolumeError">
          {{ formattedLogVolume.value }}<span class="card-unit">{{ formattedLogVolume.unit }}</span>
        </template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span class="trend-na">{{ t('dashboard.kpi.trendNotAvailable') }}</span>
      </div>
    </div>
    <!-- Active Traces -->
    <div class="card">
      <div class="card-top">
        <span class="card-label">{{ t('dashboard.kpi.activeTraces') }} · {{ _time_range_store.getLabel }}</span>
        <div class="card-top-actions">
          <RefreshButton :loading="activeTracesLoading" @refresh="getActiveTracesKPI" />
          <div class="card-icon teal">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none">
              <circle cx="5" cy="6" r="2" stroke="currentColor" stroke-width="1.8" />
              <circle cx="5" cy="18" r="2" stroke="currentColor" stroke-width="1.8" />
              <circle cx="19" cy="12" r="2" stroke="currentColor" stroke-width="1.8" />
              <path d="M7 6h6a3 3 0 013 3M7 18h6a3 3 0 003-3" stroke="currentColor" stroke-width="1.8" />
            </svg>
          </div>
        </div>
      </div>
      <Skeleton v-if="activeTracesLoading" width="90px" height="30px" />
      <div v-else class="card-value">
        <template v-if="!activeTracesError && activeTraces">{{ activeTraces.count.toLocaleString() }}</template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span :class="activeTracesTrend?.direction === 'down' ? 'trend-down' : 'trend-up'">{{ activeTracesTrend?.label ?? '' }}</span>
        <svg v-if="spark2" viewBox="0 0 100 32" width="78" height="26" preserveAspectRatio="none">
          <path :d="spark2" fill="none" stroke="#00BFA5" stroke-width="1.8" vector-effect="non-scaling-stroke"
            stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </div>
    </div>
    <!-- Metric Endpoints -->
    <div class="card">
      <div class="card-top">
        <span class="card-label">{{ t('dashboard.kpi.metricEndpoints') }} · {{ _time_range_store.getLabel }}</span>
        <div class="card-top-actions">
          <RefreshButton :loading="metricEndpointsLoading" @refresh="getMetricEndpointsKPI" />
          <div class="card-icon teal">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none">
              <path d="M4 17l4-5 3 3 4-7 5 6" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"
                stroke-linejoin="round" />
            </svg>
          </div>
        </div>
      </div>
      <Skeleton v-if="metricEndpointsLoading" width="130px" height="30px" />
      <div v-else class="card-value">
        <template v-if="!metricEndpointsError && metricEndpoints">{{ metricEndpoints.healthy }}<span class="card-unit" style="font-size:13px;color:var(--text-muted);">/ {{ t('dashboard.kpi.ofTotalHealthy', { total: metricEndpoints.total }) }}</span></template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span style="font-size:12px;font-weight:600;color:var(--text-secondary);">{{ metricEndpoints ? t('dashboard.kpi.scraping', { n: metricEndpoints.scraping }) : '' }}</span>
        <svg v-if="spark3" viewBox="0 0 100 32" width="78" height="26" preserveAspectRatio="none">
          <path :d="spark3" fill="none" stroke="#7A808C" stroke-width="1.8" vector-effect="non-scaling-stroke"
            stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      </div>
    </div>
    <!-- Error Rate -->
    <div class="card error-card">
      <div class="error-glow" />
      <div class="card-top">
        <span class="card-label">{{ t('dashboard.kpi.errorRate') }} · {{ _time_range_store.getLabel }}</span>
        <div class="card-top-actions">
          <RefreshButton :loading="errorRateLoading" @refresh="getErrorRateKPI" />
          <div class="card-icon red">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none">
              <path d="M12 4L2.5 20h19L12 4z" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" />
              <path d="M12 10v4M12 17.5v.5" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" />
            </svg>
          </div>
        </div>
      </div>
      <Skeleton v-if="errorRateLoading || logVolumeLoading" width="90px" height="30px" />
      <div v-else class="card-value" style="color:#FF8088;">
        <template v-if="!errorRateError && formattedErrorRate">
          {{ formattedErrorRate }}<span class="card-unit" style="color:#FF8088;">%</span>
        </template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span class="trend-na">{{ t('dashboard.kpi.trendNotAvailable') }}</span>
      </div>
    </div>
  </div>

  <!-- Middle row -->
  <div class="mid-grid">
    <!-- Area chart -->
    <div class="card chart-card">
      <div class="chart-hdr">
        <div>
          <div class="section-title">{{ t('dashboard.charts.logVolumeOverTime') }}</div>
          <div class="section-sub">{{ _time_range_store.getLabel }} · {{ volumeOverTimeBucketLabel }}</div>
        </div>
        <div class="chart-hdr-actions">
          <div class="legend">
            <span class="legend-item"><span class="legend-dot" style="background:#00BFA5" /><span>{{ t('dashboard.charts.logVolumeLegend') }}</span></span>
          </div>
          <RefreshButton :loading="volumeOverTimeLoading" @refresh="getVolumeOverTime" />
        </div>
      </div>
      <LogVolumeChart :data="volumeOverTime" :loading="volumeOverTimeLoading" :error="volumeOverTimeError" />
    </div>

    <!-- Top services -->
    <div class="card">
      <div class="chart-hdr" style="margin-bottom:18px;">
        <div>
          <div class="section-title">{{ t('dashboard.charts.topServicesByErrorRate') }}</div>
          <div class="section-sub">{{ _time_range_store.getLabel }}</div>
        </div>
        <RefreshButton :loading="errorRateByServiceLoading" @refresh="getErrorRateByServiceKPI" />
      </div>
      <ErrorRateChart :data="errorRateByService" :loading="errorRateByServiceLoading" :error="errorRateByServiceError" />
    </div>
  </div>

  <!-- Bottom row -->
  <div class="bot-grid">
    <!-- Heatmap -->
    <div class="card">
      <div class="chart-hdr" style="margin-bottom:18px;">
        <div>
          <div class="section-title">{{ t('dashboard.charts.traceLatencyHeatmap') }}</div>
          <div class="section-sub">p50 / p95 / p99 · {{ heatmapBucketLabel }} · ms</div>
        </div>
        <RefreshButton :loading="latencyHeatmapLoading" @refresh="getLatencyHeatmap" />
      </div>
      <TraceLatencyHeatmap :data="latencyHeatmap" :loading="latencyHeatmapLoading" :error="latencyHeatmapError" :bucket-hours="heatmapBucketHours" />
    </div>

    <!-- Recent Logs -->
    <div class="card">
      <div class="chart-hdr" style="margin-bottom:14px;">
        <div style="display:flex;align-items:center;gap:9px;">
          <span class="section-title">{{ t('dashboard.recentLogs.title') }}</span>
          <span class="live-tag">{{ t('dashboard.recentLogs.last1h') }}</span>
          <span class="live-tag" :class="{ 'live-tag-off': !logStreamConnected }">
            <span class="live-tag-dot" />{{ logStreamConnected ? t('dashboard.recentLogs.live') : t('dashboard.recentLogs.connecting') }}
          </span>
        </div>
        <div class="chart-hdr-actions">
          <RefreshButton :loading="recentCriticalLogsLoading" @refresh="getRecentCriticalLogs" />
          <RouterLink to="/logs" class="link-btn">{{ t('dashboard.recentLogs.openInLogs') }}</RouterLink>
        </div>
      </div>
      <div v-if="recentCriticalLogsLoading" class="log-list" style="gap:8px;">
        <Skeleton v-for="i in 4" :key="i" height="34px" />
      </div>
      <div v-else-if="recentCriticalLogs.length === 0" class="not-available-panel">
        <span>{{ t('dashboard.recentLogs.empty') }}</span>
      </div>
      <div v-else class="log-list">
        <div v-for="log in recentCriticalLogs" :key="log.id" class="log-row" :style="{ borderInlineStartColor: log.color }">
          <span class="log-time font-mono">{{ log.time }}</span>
          <span class="log-level font-mono" :style="{ color: log.color, background: log.bg }">{{ log.level }}</span>
          <span class="log-svc font-mono" style="color:var(--accent-bright)">{{ log.service }}</span>
          <span class="log-msg font-mono">{{ log.msg }}</span>
        </div>
      </div>
    </div>
  </div>
</div>
</template>

<style scoped>
/* Root: the dashboard owns its own scroll behavior instead of relying on
   DefaultLayout's page-level overflow-y:auto (that's still there as a
   fallback for pages like settings that DO want whole-page scroll). Top
   sections keep their natural height; .bot-grid fills whatever's left and
   only the Recent Critical Logs list scrolls internally, so a full log
   list never pushes the KPI/chart rows off screen or grows the page. */
.dashboard-page {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 18px;
  overflow: hidden;
}

.page-hdr,
.kpi-grid,
.mid-grid {
  flex-shrink: 0;
}

.glow-orb {
  position: fixed;
  top: -120px;
  left: 120px;
  width: 520px;
  height: 420px;
  background: radial-gradient(ellipse at center, rgba(0, 191, 165, 0.08), transparent 70%);
  pointer-events: none;
  z-index: 0;
}

.page-hdr {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
}

.page-title {
  margin: 0;
  font-size: 23px;
  font-weight: 700;
  letter-spacing: -0.5px;
  color: var(--text-primary);
}

.page-sub {
  margin: 5px 0 0;
  font-size: 13px;
  color: var(--text-secondary);
}

.page-sub code {
  font-family: 'IBM Plex Mono', monospace;
  color: var(--text-secondary);
}

.hdr-actions {
  display: flex;
  align-items: center;
  gap: 9px;
}

.hdr-select-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.hdr-select-label {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.6px;
  text-transform: uppercase;
  color: var(--text-muted);
  padding-inline-start: 1px;
}

.hdr-select {
  min-width: 150px;
}

.dot-green {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent);
}

/* KPI */
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
}

.card {
  background: var(--bg-card);
  border: 1px solid var(--border-subtle);
  border-radius: 14px;
  padding: 17px;
  backdrop-filter: blur(10px);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
  display: flex;
  flex-direction: column;
  gap: 13px;
}

.error-card {
  border-color: rgba(240, 97, 109, 0.16);
  position: relative;
  overflow: hidden;
}

.error-glow {
  position: absolute;
  top: -40px;
  right: -40px;
  width: 120px;
  height: 120px;
  background: radial-gradient(circle, rgba(240, 97, 109, 0.10), transparent 70%);
}

.card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.card-top-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.card-label {
  font-size: 12.5px;
  font-weight: 500;
  color: var(--text-secondary);
}

.card-icon {
  width: 30px;
  height: 30px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.card-icon.teal {
  background: rgba(0, 191, 165, 0.12);
  color: var(--accent-bright);
}

.card-icon.red {
  background: rgba(240, 97, 109, 0.14);
  color: #FF8088;
}

.card-value {
  font-size: 30px;
  font-weight: 700;
  letter-spacing: -0.8px;
  color: var(--text-primary);
}

.card-unit {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-secondary);
  margin-inline-start: 4px;
}

.kpi-error {
  color: var(--text-muted);
}

.card-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 18px;
}

.trend-up {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-bright);
}

.trend-down {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  font-size: 12px;
  font-weight: 600;
  color: #FF8088;
}

.trend-na {
  font-size: 11.5px;
  font-weight: 500;
  color: var(--text-muted);
}

/* Middle */
.mid-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 16px;
}

.chart-card {
  gap: 0;
}

.chart-hdr {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.chart-hdr-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.section-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.section-sub {
  font-size: 11.5px;
  color: var(--text-muted);
  margin-top: 3px;
}

.legend {
  display: flex;
  align-items: center;
  gap: 16px;
}

.legend-item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 11.5px;
  color: var(--text-secondary);
}

.legend-dot {
  width: 9px;
  height: 9px;
  border-radius: 3px;
}

.link-btn {
  font-size: 12px;
  color: var(--accent-bright);
  font-weight: 500;
  cursor: pointer;
  text-decoration: none;
}

/* Bottom */
.bot-grid {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 1fr 1fr;
  grid-template-rows: 1fr;
  gap: 16px;
}

/* Scoped to .bot-grid so KPI/mid-grid cards (which size to their own
   content and never overflow) are unaffected. */
.bot-grid .card {
  min-height: 0;
  overflow: hidden;
}

.log-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-inline-end: 4px;
}

.not-available-panel {
  flex: 1;
  min-height: 160px;
  display: flex;
  align-items: center;
  justify-content: center;
  text-align: center;
  color: rgba(255, 255, 255, 0.2);
  font-size: 12px;
  font-family: 'IBM Plex Mono', monospace;
  border: 1px dashed rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  padding: 16px;
}

.live-tag {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 10px;
  font-weight: 600;
  color: var(--accent-bright);
  background: var(--accent-dim);
  padding: 2px 7px;
  border-radius: 5px;
}

.live-tag-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

.live-tag-off {
  color: var(--text-muted);
  background: var(--bg-hover);
}

.log-row {
  display: flex;
  align-items: flex-start;
  gap: 11px;
  padding: 8px 9px;
  border-radius: 7px;
  border-inline-start: 2px solid transparent;
  background: rgba(255, 255, 255, 0.015);
  transition: background 0.12s;
}

.log-row:hover {
  background: rgba(255, 255, 255, 0.04);
}

.log-time {
  font-size: 11px;
  color: var(--text-muted);
  flex-shrink: 0;
  padding-top: 1px;
}

.log-level {
  font-size: 9.5px;
  font-weight: 700;
  letter-spacing: 0.4px;
  padding: 2px 6px;
  border-radius: 4px;
  flex-shrink: 0;
  width: 46px;
  text-align: center;
}

.log-svc {
  font-size: 11px;
  flex-shrink: 0;
  padding-top: 1px;
}

.log-msg {
  font-size: 11.5px;
  color: var(--text-secondary);
  line-height: 1.4;
}

/* Responsive — below this width there's no room left to keep the
   fixed-height/internal-scroll layout above, so the dashboard falls back
   to DefaultLayout's normal whole-page scroll (same as every other page)
   instead of cramming a full dashboard into a short viewport. */
@media (max-width: 1100px) {
  .dashboard-page {
    height: auto;
    overflow: visible;
  }

  .kpi-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .mid-grid {
    grid-template-columns: 1fr;
  }

  .bot-grid {
    flex: initial;
    grid-template-columns: 1fr;
    grid-template-rows: none;
  }

  .bot-grid .card {
    min-height: 280px;
  }

  .log-list {
    max-height: 280px;
  }
}

@media (max-width: 640px) {
  .page-hdr {
    flex-direction: column;
    align-items: flex-start;
    gap: 12px;
  }

  .hdr-actions {
    flex-wrap: wrap;
  }

  .kpi-grid {
    grid-template-columns: 1fr;
  }

  .log-row {
    flex-wrap: wrap;
  }

  .log-msg {
    flex-basis: 100%;
  }
}
</style>
