<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Skeleton from 'primevue/skeleton'
import MetricService from '@/services/metric.service'
import { useApiError } from '@/composables/useApiError'

const { t } = useI18n()
const _metric_service: MetricService = new MetricService();
const { notifyError } = useApiError()

// SVG line chart helper (same pattern as dashboard, no extra packages needed).
// `|| 1` on the x-divisor guards a single-point series (vals.length === 1,
// e.g. a brand-new/low-traffic tenant with only one bucket of data so far):
// without it, i / (vals.length - 1) is 0/0 = NaN, producing an invalid
// "MNaN ..." path that Chart -- er, the raw <path> -- refuses to render.
function sparkLine(vals: number[], w = 1000, h = 200): string {
  const mx = Math.max(...vals), mn = Math.min(...vals), range = (mx - mn) || 1
  const xDivisor = (vals.length - 1) || 1
  return vals.map((v, i) =>
    (i ? 'L' : 'M') + ((i / xDivisor) * w).toFixed(1) + ' ' + (h - ((v - mn) / range) * (h - 20) - 10).toFixed(1)
  ).join(' ')
}

const overviewLoading = ref(false)
const overviewError = ref(false)
const overview = ref<{
  cpuUsage: number; cpuUsageChange: number
  memoryUsage: number; memoryUsageChange: number
  requestRate: number; requestRateChange: number
  p99Latency: number; p99LatencyChange: number
} | null>(null)

const cpuTrendLoading = ref(false)
const cpuTrendError = ref(false)
const cpuTrend = ref<{ timestamp: string; value: number }[] | null>(null)

const memTrendLoading = ref(false)
const memTrendError = ref(false)
const memTrend = ref<{ timestamp: string; value: number }[] | null>(null)

const serviceOverviewLoading = ref(false)
const serviceOverviewError = ref(false)
const serviceOverview = ref<{ serviceName: string; avgCpu: number; avgMemory: number; requestRate: number; errorRate: number }[] | null>(null)

function getMetricsOverviewKPI() {
  const now = new Date();
  const oneHourAgo = new Date(now.getTime() - 60 * 60 * 1000)
  const from = encodeURIComponent(oneHourAgo.toISOString());
  const to = encodeURIComponent(now.toISOString());
  overviewLoading.value = true;
  overviewError.value = false;
  return _metric_service.GetMetricsOverviewKPI(from, to).then(response => {
    overview.value = response ?? null;
  }).catch(err => {
    console.error('Failed to load metrics overview', err);
    overview.value = null;
    overviewError.value = true;
    notifyError('Metrics Overview');
  }).finally(() => {
    overviewLoading.value = false;
  });
}

function getCpuUsageTrend() {
  const now = new Date();
  const oneDayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000)
  const from = encodeURIComponent(oneDayAgo.toISOString());
  const to = encodeURIComponent(now.toISOString());
  cpuTrendLoading.value = true;
  cpuTrendError.value = false;
  return _metric_service.GetCpuUsageTrend(from, to, 30).then(response => {
    cpuTrend.value = response?.data ?? null;
  }).catch(err => {
    console.error('Failed to load CPU usage trend', err);
    cpuTrend.value = null;
    cpuTrendError.value = true;
    notifyError('CPU Usage');
  }).finally(() => {
    cpuTrendLoading.value = false;
  });
}

function getMemoryUsageTrend() {
  const now = new Date();
  const oneDayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000)
  const from = encodeURIComponent(oneDayAgo.toISOString());
  const to = encodeURIComponent(now.toISOString());
  memTrendLoading.value = true;
  memTrendError.value = false;
  return _metric_service.GetMemoryUsageTrend(from, to, 30).then(response => {
    memTrend.value = response?.data ?? null;
  }).catch(err => {
    console.error('Failed to load memory usage trend', err);
    memTrend.value = null;
    memTrendError.value = true;
    notifyError('Memory Usage');
  }).finally(() => {
    memTrendLoading.value = false;
  });
}

function getServiceOverviewKPI() {
  const now = new Date();
  const oneHourAgo = new Date(now.getTime() - 60 * 60 * 1000)
  const from = encodeURIComponent(oneHourAgo.toISOString());
  const to = encodeURIComponent(now.toISOString());
  serviceOverviewLoading.value = true;
  serviceOverviewError.value = false;
  return _metric_service.GetServiceOverviewKPI(from, to).then(response => {
    serviceOverview.value = response?.data ?? null;
  }).catch(err => {
    console.error('Failed to load service overview', err);
    serviceOverview.value = null;
    serviceOverviewError.value = true;
    notifyError('Service Overview');
  }).finally(() => {
    serviceOverviewLoading.value = false;
  });
}

onMounted(() => {
  getMetricsOverviewKPI();
  getCpuUsageTrend();
  getMemoryUsageTrend();
  getServiceOverviewKPI();
})

const cpuPath = computed(() => cpuTrend.value?.length ? sparkLine(cpuTrend.value.map(p => p.value)) : '')
const memPath = computed(() => memTrend.value?.length ? sparkLine(memTrend.value.map(p => p.value)) : '')

// Y-axis ticks (CPU/Memory usage are reported as 0-100 percentages)
const cpuYTicks = [80, 60, 40, 20, 0]
const memYTicks = [100, 80, 60, 40, 20]

function trendLabel(change: number): string {
  return `${change >= 0 ? '↑' : '↓'} ${Math.abs(change).toFixed(1)}%`
}

// Request rate is requests/sec from the BFF; format like the dashboard's log-volume KPI.
const formattedRequestRate = computed(() => {
  const n = overview.value?.requestRate ?? 0
  if (n >= 1_000_000) return { value: (n / 1_000_000).toFixed(2), unit: 'M/s' }
  if (n >= 1_000) return { value: (n / 1_000).toFixed(1), unit: 'K/s' }
  return { value: n.toFixed(1), unit: '/s' }
})

// P99Latency comes back in seconds (OTel duration convention); display in ms.
const formattedP99Latency = computed(() => {
  if (overview.value === null) return null
  return Math.round(overview.value.p99Latency * 1000)
})

function errorRateColor(errorRate: number): string {
  if (errorRate >= 3) return '#F0616D'
  if (errorRate >= 1.5) return '#E8B23A'
  return '#2DD4BF'
}
</script>

<template>
  <div class="metrics-page">
  <!-- Page header -->
  <div class="page-hdr">
    <div>
      <h1 class="page-title">{{ t('metrics.title') }}</h1>
      <p class="page-sub">{{ t('metrics.subtitle') }} · <code>us-east-1</code></p>
    </div>
    <div class="hdr-actions">
      <button class="action-btn">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none"><path d="M3 6h18M7 12h10M10 18h4" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        {{ t('logs.filter') }}
      </button>
      <button class="action-btn">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none"><path d="M12 3v12m0 0l-4-4m4 4l4-4M5 21h14" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
        {{ t('logs.export') }}
      </button>
    </div>
  </div>

  <!-- KPI row -->
  <div class="kpi-grid">
    <div class="card">
      <div class="card-top">
        <span class="card-label">{{ t('metrics.kpi.cpuUsage') }}</span>
        <div class="card-icon teal">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none"><rect x="4" y="4" width="16" height="16" rx="2" stroke="currentColor" stroke-width="1.8"/><path d="M9 9h6v6H9z" stroke="currentColor" stroke-width="1.8"/><path d="M9 2v2M15 2v2M9 20v2M15 20v2M2 9h2M2 15h2M20 9h2M20 15h2" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>
        </div>
      </div>
      <Skeleton v-if="overviewLoading" width="70px" height="30px" />
      <div v-else class="card-value">
        <template v-if="overview">{{ overview.cpuUsage.toFixed(0) }}<span class="card-unit">%</span></template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span class="trend-warn">{{ overview ? trendLabel(overview.cpuUsageChange) : '' }}</span>
      </div>
    </div>
    <div class="card">
      <div class="card-top">
        <span class="card-label">{{ t('metrics.kpi.memoryUsage') }}</span>
        <div class="card-icon teal">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none"><path d="M5 3h14a2 2 0 012 2v14a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2z" stroke="currentColor" stroke-width="1.8"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18" stroke="currentColor" stroke-width="1.8"/></svg>
        </div>
      </div>
      <Skeleton v-if="overviewLoading" width="70px" height="30px" />
      <div v-else class="card-value">
        <template v-if="overview">{{ overview.memoryUsage.toFixed(0) }}<span class="card-unit">%</span></template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span class="trend-warn">{{ overview ? trendLabel(overview.memoryUsageChange) : '' }}</span>
      </div>
    </div>
    <div class="card">
      <div class="card-top">
        <span class="card-label">{{ t('metrics.kpi.requestRate') }}</span>
        <div class="card-icon teal">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none"><path d="M4 17l4-5 3 3 4-7 5 6" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
        </div>
      </div>
      <Skeleton v-if="overviewLoading" width="90px" height="30px" />
      <div v-else class="card-value">
        <template v-if="overview">{{ formattedRequestRate.value }}<span class="card-unit">{{ formattedRequestRate.unit }}</span></template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span class="trend-up">{{ overview ? trendLabel(overview.requestRateChange) : '' }}</span>
      </div>
    </div>
    <div class="card">
      <div class="card-top">
        <span class="card-label">{{ t('metrics.kpi.p99Latency') }}</span>
        <div class="card-icon teal">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="8.5" stroke="currentColor" stroke-width="1.8"/><path d="M12 8v4l3 2" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>
        </div>
      </div>
      <Skeleton v-if="overviewLoading" width="70px" height="30px" />
      <div v-else class="card-value">
        <template v-if="overview">{{ formattedP99Latency }}<span class="card-unit">ms</span></template>
        <span v-else class="kpi-error">—</span>
      </div>
      <div class="card-bottom">
        <span class="trend-up">{{ overview ? trendLabel(overview.p99LatencyChange) : '' }}</span>
      </div>
    </div>
  </div>

  <!-- Charts row -->
  <div class="chart-grid">
    <!-- CPU chart -->
    <div class="card chart-card">
      <div class="chart-hdr">
        <div>
          <div class="section-title">{{ t('metrics.charts.cpuTitle') }}</div>
          <div class="section-sub">{{ t('metrics.charts.subtitle') }}</div>
        </div>
        <span class="legend-item"><span class="legend-dot" style="background:#00BFA5"/>{{ t('metrics.kpi.cpuUsage') }}</span>
      </div>
      <Skeleton v-if="cpuTrendLoading" width="100%" height="160px" />
      <div v-else-if="cpuPath" style="display:flex;gap:8px;">
        <div class="y-axis">
          <span v-for="tick in cpuYTicks" :key="tick" class="y-tick">{{ tick }}%</span>
        </div>
        <div style="flex:1">
          <svg viewBox="0 0 1000 200" width="100%" height="160" preserveAspectRatio="none" style="display:block">
            <line v-for="y in [0,50,100,150,200]" :key="y" x1="0" :y1="y" x2="1000" :y2="y" stroke="rgba(255,255,255,0.05)" stroke-width="1" vector-effect="non-scaling-stroke"/>
            <path :d="cpuPath + ' L1000 200 L0 200 Z'" fill="rgba(0,191,165,0.10)" stroke="none"/>
            <path :d="cpuPath" fill="none" stroke="#00BFA5" stroke-width="2" vector-effect="non-scaling-stroke" stroke-linejoin="round"/>
          </svg>
          <div class="x-labels">
            <span>{{ t('metrics.charts.xAxis.24hAgo') }}</span><span>{{ t('metrics.charts.xAxis.18h') }}</span><span>{{ t('metrics.charts.xAxis.12h') }}</span><span>{{ t('metrics.charts.xAxis.6h') }}</span><span style="color:var(--accent-bright)">{{ t('metrics.charts.xAxis.now') }}</span>
          </div>
        </div>
      </div>
      <div v-else class="chart-placeholder">{{ cpuTrendError ? t('metrics.charts.cpuFailed') : t('charts.noData') }}</div>
    </div>

    <!-- Memory chart -->
    <div class="card chart-card">
      <div class="chart-hdr">
        <div>
          <div class="section-title">{{ t('metrics.charts.memTitle') }}</div>
          <div class="section-sub">{{ t('metrics.charts.subtitle') }}</div>
        </div>
        <span class="legend-item"><span class="legend-dot" style="background:#E8B23A"/>{{ t('metrics.kpi.memoryUsage') }}</span>
      </div>
      <Skeleton v-if="memTrendLoading" width="100%" height="160px" />
      <div v-else-if="memPath" style="display:flex;gap:8px;">
        <div class="y-axis">
          <span v-for="tick in memYTicks" :key="tick" class="y-tick">{{ tick }}%</span>
        </div>
        <div style="flex:1">
          <svg viewBox="0 0 1000 200" width="100%" height="160" preserveAspectRatio="none" style="display:block">
            <line v-for="y in [0,50,100,150,200]" :key="y" x1="0" :y1="y" x2="1000" :y2="y" stroke="rgba(255,255,255,0.05)" stroke-width="1" vector-effect="non-scaling-stroke"/>
            <path :d="memPath + ' L1000 200 L0 200 Z'" fill="rgba(232,178,58,0.10)" stroke="none"/>
            <path :d="memPath" fill="none" stroke="#E8B23A" stroke-width="2" vector-effect="non-scaling-stroke" stroke-linejoin="round"/>
          </svg>
          <div class="x-labels">
            <span>{{ t('metrics.charts.xAxis.24hAgo') }}</span><span>{{ t('metrics.charts.xAxis.18h') }}</span><span>{{ t('metrics.charts.xAxis.12h') }}</span><span>{{ t('metrics.charts.xAxis.6h') }}</span><span style="color:var(--accent-bright)">{{ t('metrics.charts.xAxis.now') }}</span>
          </div>
        </div>
      </div>
      <div v-else class="chart-placeholder">{{ memTrendError ? t('metrics.charts.memFailed') : t('charts.noData') }}</div>
    </div>
  </div>

  <!-- Services table -->
  <div class="card table-card">
    <div class="section-title" style="margin-bottom:14px;">{{ t('metrics.serviceOverview') }}</div>
    <DataTable :value="serviceOverview ?? []" :loading="serviceOverviewLoading" row-hover scrollable scroll-height="flex" class="metrics-table">
      <template #loading>
        <div style="display:flex;flex-direction:column;gap:8px;padding:14px;">
          <Skeleton v-for="i in 5" :key="i" height="28px" />
        </div>
      </template>
      <template #empty>
        <div class="table-empty">{{ serviceOverviewError ? t('metrics.loadFailed') : t('metrics.noneFound') }}</div>
      </template>
      <Column field="serviceName" :header="t('logs.columns.service')">
        <template #body="{ data }">
          <span class="font-mono svc-name">{{ data.serviceName }}</span>
        </template>
      </Column>
      <Column field="avgCpu" :header="t('metrics.columns.avgCpu')">
        <template #body="{ data }">
          <span class="font-mono metric-val">{{ data.avgCpu.toFixed(0) }}%</span>
        </template>
      </Column>
      <Column field="avgMemory" :header="t('metrics.columns.avgMemory')">
        <template #body="{ data }">
          <span class="font-mono metric-val">{{ data.avgMemory.toFixed(0) }}%</span>
        </template>
      </Column>
      <Column field="requestRate" :header="t('metrics.kpi.requestRate')">
        <template #body="{ data }">
          <span class="font-mono metric-val">{{ data.requestRate.toFixed(1) }}/s</span>
        </template>
      </Column>
      <Column field="errorRate" :header="t('dashboard.kpi.errorRate')">
        <template #body="{ data }">
          <span class="font-mono" :style="{ color: errorRateColor(data.errorRate), fontWeight: 600 }">{{ data.errorRate.toFixed(1) }}%</span>
        </template>
      </Column>
    </DataTable>
  </div>
  </div>
</template>

<style scoped>
.metrics-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  gap: 16px;
}

.page-hdr { flex-shrink: 0; display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; }
.page-title { margin: 0; font-size: 23px; font-weight: 700; letter-spacing: -0.5px; color: var(--text-primary); }
.page-sub   { margin: 5px 0 0; font-size: 13px; color: var(--text-secondary); }
.page-sub code { font-family: 'IBM Plex Mono', monospace; color: var(--text-secondary); }
.hdr-actions { display: flex; gap: 9px; }
.action-btn {
  display: flex; align-items: center; gap: 8px; padding: 8px 12px;
  border: 1px solid var(--border-medium); border-radius: 9px; font-size: 12.5px; font-weight: 500;
  color: var(--text-primary); background: var(--bg-hover); cursor: pointer; transition: border-color 0.15s;
}
.action-btn:hover { border-color: var(--border-medium); }

.kpi-grid  { flex-shrink: 0; display: grid; grid-template-columns: repeat(4,1fr); gap: 16px; }
.chart-grid{ flex-shrink: 0; display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }

.card {
  background: var(--bg-card); border: 1px solid var(--border-subtle); border-radius: 14px;
  padding: 17px; backdrop-filter: blur(10px); box-shadow: inset 0 1px 0 rgba(255,255,255,0.04);
  display: flex; flex-direction: column; gap: 13px;
}
.chart-card { gap: 0; }
.table-card { flex: 1; min-height: 0; padding: 18px; gap: 0; overflow: hidden; }

.card-top   { display: flex; align-items: center; justify-content: space-between; }
.card-label { font-size: 12.5px; font-weight: 500; color: var(--text-secondary); }
.card-icon.teal { width: 30px; height: 30px; border-radius: 8px; background: rgba(0,191,165,0.12); color: var(--accent-bright); display: flex; align-items: center; justify-content: center; }
.card-value { font-size: 30px; font-weight: 700; letter-spacing: -0.8px; color: var(--text-primary); }
.card-unit  { font-size: 16px; font-weight: 600; color: var(--text-secondary); margin-inline-start: 4px; }
.card-bottom{ display: flex; min-height: 16px; }
.trend-up   { font-size: 12px; font-weight: 600; color: var(--accent-bright); }
.trend-warn { font-size: 12px; font-weight: 600; color: var(--warn); }
.kpi-error  { color: var(--text-muted); }

.chart-hdr  { display: flex; align-items: center; justify-content: space-between; margin-bottom: 14px; }
.section-title { font-size: 14px; font-weight: 600; color: var(--text-primary); }
.section-sub   { font-size: 11.5px; color: var(--text-muted); margin-top: 3px; }
.legend-item   { display: inline-flex; align-items: center; gap: 6px; font-size: 11.5px; color: var(--text-secondary); }
.legend-dot    { width: 9px; height: 9px; border-radius: 3px; }

.y-axis { width: 38px; height: 160px; display: flex; flex-direction: column; justify-content: space-between; align-items: flex-end; padding: 2px 0; }
.y-tick { font-size: 10px; font-family: 'IBM Plex Mono', monospace; color: var(--text-muted); }
.x-labels { display: flex; justify-content: space-between; margin-top: 6px; font-size: 10px; font-family: 'IBM Plex Mono', monospace; color: var(--text-muted); }

.chart-placeholder {
  display: flex; align-items: center; justify-content: center; height: 160px;
  color: rgba(255, 255, 255, 0.2); font-size: 12px; font-family: 'IBM Plex Mono', monospace;
  border: 1px dashed rgba(255, 255, 255, 0.08); border-radius: 8px;
}

.table-empty { padding: 24px; text-align: center; font-size: 12px; font-family: 'IBM Plex Mono', monospace; color: rgba(255, 255, 255, 0.2); }

.font-mono { font-family: 'IBM Plex Mono', monospace; }
.svc-name  { font-size: 13px; color: var(--accent-bright); }
.metric-val{ font-size: 13px; color: var(--text-primary); }

:deep(.metrics-table.p-datatable .p-datatable-thead > tr > th) {
  background: rgba(255,255,255,0.02); border-bottom: 1px solid var(--border-subtle);
  color: var(--text-secondary); font-size: 11px; font-weight: 600; letter-spacing: 0.5px; text-transform: uppercase; padding: 10px 14px;
}
:deep(.metrics-table.p-datatable .p-datatable-tbody > tr > td) {
  border-bottom: 1px solid var(--border-subtle); padding: 10px 14px; background: transparent; color: var(--text-primary);
}
:deep(.metrics-table.p-datatable .p-datatable-tbody > tr:hover > td) { background: var(--bg-hover); }
</style>
