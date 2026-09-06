<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Skeleton from 'primevue/skeleton'
import TraceService from '@/services/trace.service'
import { useApiError } from '@/composables/useApiError'
import type { TraceSummaryViewModel } from '@/models/dashboard.vm'

const { t } = useI18n()
const _trace_service: TraceService = new TraceService();
const { notifyError } = useApiError()

const overviewLoading = ref(false)
const overviewError = ref(false)
const overview = ref<{
  totalTraces: number; totalTracesChange: number
  avgDuration: number; avgDurationChange: number
  errorTraces: number; errorTracesChange: number
  p95Duration: number; p95DurationChange: number
} | null>(null)

function getTracesOverviewKPI() {
  const now = new Date();
  const oneHourAgo = new Date(now.getTime() - 60 * 60 * 1000)
  const from = encodeURIComponent(oneHourAgo.toISOString());
  const to = encodeURIComponent(now.toISOString());
  overviewLoading.value = true;
  overviewError.value = false;
  return _trace_service.GetTracesOverviewKPI(from, to).then(response => {
    overview.value = response ?? null;
  }).catch(err => {
    console.error('Failed to load traces overview', err);
    overview.value = null;
    overviewError.value = true;
    notifyError('Traces Overview');
  }).finally(() => {
    overviewLoading.value = false;
  });
}

const serviceFilter = ref('')
const traceIdFilter = ref('')

const searchLoading = ref(false)
const searchError = ref(false)
const searchResults = ref<TraceSummaryViewModel[]>([])
const totalCount = ref(0)
const page = ref(1)
const pageSize = ref(50)

function fetchSearchResults() {
  const now = new Date();
  const oneDayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000)
  const from = encodeURIComponent(oneDayAgo.toISOString());
  const to = encodeURIComponent(now.toISOString());
  searchLoading.value = true;
  searchError.value = false;
  return _trace_service.SearchTraces(from, to, serviceFilter.value || undefined, traceIdFilter.value || undefined, page.value, pageSize.value).then(response => {
    searchResults.value = response?.data ?? [];
    totalCount.value = response?.totalCount ?? 0;
  }).catch(err => {
    console.error('Failed to search traces', err);
    searchResults.value = [];
    totalCount.value = 0;
    searchError.value = true;
    notifyError('Trace Search');
  }).finally(() => {
    searchLoading.value = false;
  });
}

function onPage(event: { page: number; rows: number }) {
  page.value = event.page + 1
  pageSize.value = event.rows
  fetchSearchResults()
}

let searchDebounce: ReturnType<typeof setTimeout> | undefined
watch([serviceFilter, traceIdFilter], () => {
  clearTimeout(searchDebounce)
  searchDebounce = setTimeout(() => {
    page.value = 1
    fetchSearchResults()
  }, 400)
})

function clearFilters() {
  serviceFilter.value = ''
  traceIdFilter.value = ''
}

onMounted(() => {
  getTracesOverviewKPI()
  fetchSearchResults()
})

interface TraceRow {
  id: number
  traceId: string
  timestamp: string
  service: string
  spanName: string
  durationMs: number
  status: 'success' | 'error'
}

// Only root spans are returned (one row per trace) — there is no per-span
// breakdown endpoint, so the expandable span list from the old mock is gone.
const displayedTraces = computed<TraceRow[]>(() =>
  searchResults.value.map((t, idx) => ({
    id: (page.value - 1) * pageSize.value + idx,
    traceId: t.traceId,
    timestamp: t.timestamp,
    service: t.serviceName,
    spanName: t.spanName,
    durationMs: t.duration / 1_000_000,
    status: t.statusCode === 'Error' ? 'error' : 'success',
  }))
)

function formatDuration(ms: number): string {
  return `${Math.round(ms).toLocaleString()}ms`
}
</script>

<template>
  <div class="traces-page">
  <!-- Page header -->
  <div class="page-hdr">
    <div>
      <h1 class="page-title">{{ t('traces.title') }}</h1>
      <p class="page-sub">{{ t('traces.subtitle') }} · <code>us-east-1</code></p>
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

  <!-- Stats bar -->
  <div class="stats-bar">
    <div class="stat-chip">
      <span class="stat-label">{{ t('traces.stats.totalTraces') }}</span>
      <Skeleton v-if="overviewLoading" width="50px" height="18px" />
      <span v-else class="stat-value font-mono">{{ overview && !overviewError ? overview.totalTraces.toLocaleString() : '—' }}</span>
    </div>
    <div class="stat-chip">
      <span class="stat-label">{{ t('traces.stats.avgDuration') }}</span>
      <Skeleton v-if="overviewLoading" width="50px" height="18px" />
      <span v-else class="stat-value font-mono">{{ overview && !overviewError ? formatDuration(overview.avgDuration) : '—' }}</span>
    </div>
    <div class="stat-chip">
      <span class="stat-label">{{ t('traces.stats.errorTraces') }}</span>
      <Skeleton v-if="overviewLoading" width="50px" height="18px" />
      <span v-else class="stat-value font-mono" style="color:#F0616D;">{{ overview && !overviewError ? overview.errorTraces.toLocaleString() : '—' }}</span>
    </div>
    <div class="stat-chip">
      <span class="stat-label">{{ t('traces.stats.p95Duration') }}</span>
      <Skeleton v-if="overviewLoading" width="50px" height="18px" />
      <span v-else class="stat-value font-mono">{{ overview && !overviewError ? formatDuration(overview.p95Duration) : '—' }}</span>
    </div>
  </div>

  <!-- Filter toolbar -->
  <div class="card toolbar">
    <InputText
      v-model="serviceFilter"
      :placeholder="t('logs.filterByService')"
      class="filter-select"
    />
    <InputText
      v-model="traceIdFilter"
      :placeholder="t('traces.searchByTraceId')"
      class="filter-input"
    />
    <div style="margin-inline-start:auto;">
      <Button :label="t('traces.clear')" severity="secondary" outlined size="small" @click="clearFilters()" />
    </div>
  </div>

  <!-- Traces table -->
  <div class="card table-card">
    <DataTable
      :value="displayedTraces"
      row-hover
      scrollable
      scroll-height="flex"
      class="traces-table"
      :loading="searchLoading"
      lazy
      paginator
      :rows="pageSize"
      :total-records="totalCount"
      @page="onPage"
    >
      <template #loading>
        <div style="display:flex;flex-direction:column;gap:8px;padding:14px;">
          <Skeleton v-for="i in 8" :key="i" height="30px" />
        </div>
      </template>
      <template #empty>
        <div class="table-empty">{{ searchError ? t('traces.loadFailed') : t('traces.noneFound') }}</div>
      </template>
      <Column field="traceId" :header="t('logs.columns.traceId')" style="width:130px">
        <template #body="{ data }">
          <span class="font-mono cell-traceid">{{ data.traceId }}</span>
        </template>
      </Column>

      <Column field="service" :header="t('logs.columns.service')" style="width:140px">
        <template #body="{ data }">
          <span class="font-mono cell-service">{{ data.service }}</span>
        </template>
      </Column>

      <Column field="spanName" :header="t('traces.rootSpan')">
        <template #body="{ data }">
          <span class="font-mono cell-muted">{{ data.spanName }}</span>
        </template>
      </Column>

      <Column field="durationMs" :header="t('traces.duration')" style="width:100px">
        <template #body="{ data }">
          <span class="font-mono cell-duration">{{ formatDuration(data.durationMs) }}</span>
        </template>
      </Column>

      <Column field="status" :header="t('traces.status.label')" style="width:90px">
        <template #body="{ data }">
          <span
            class="status-badge font-mono"
            :class="data.status"
          >{{ data.status === 'error' ? t('traces.status.error') : t('traces.status.success') }}</span>
        </template>
      </Column>

      <Column field="timestamp" :header="t('logs.columns.timestamp')">
        <template #body="{ data }">
          <span class="font-mono cell-muted">{{ data.timestamp }}</span>
        </template>
      </Column>
    </DataTable>
  </div>
  </div>
</template>

<style scoped>
.traces-page {
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

.stats-bar { flex-shrink: 0; display: flex; gap: 12px; }
.stat-chip {
  display: flex; align-items: center; gap: 8px;
  background: var(--bg-card); border: 1px solid var(--border-subtle); border-radius: 10px;
  padding: 10px 16px; backdrop-filter: blur(10px);
}
.stat-label { font-size: 12px; color: var(--text-secondary); }
.stat-value { font-size: 15px; font-weight: 700; color: var(--text-primary); }

.card {
  background: var(--bg-card); border: 1px solid var(--border-subtle); border-radius: 14px;
  backdrop-filter: blur(10px); box-shadow: inset 0 1px 0 rgba(255,255,255,0.04);
}
.toolbar {
  flex-shrink: 0; display: flex; align-items: center; gap: 10px; padding: 14px 16px; flex-wrap: wrap;
}
.filter-select { min-width: 160px; }
.filter-input  { flex: 1; min-width: 200px; }
.table-card { flex: 1; min-height: 0; padding: 0; overflow: hidden; }

.table-empty { padding: 24px; text-align: center; font-size: 12px; font-family: 'IBM Plex Mono', monospace; color: rgba(255, 255, 255, 0.2); }

.font-mono { font-family: 'IBM Plex Mono', monospace; }
.cell-traceid { font-size: 11.5px; color: var(--text-secondary); }
.cell-service { font-size: 12px; color: var(--accent-bright); }
.cell-duration{ font-size: 12px; color: var(--text-primary); font-weight: 600; }
.cell-muted   { font-size: 11.5px; color: var(--text-muted); }

.status-badge {
  font-size: 10px; font-weight: 700; letter-spacing: 0.4px;
  padding: 2px 8px; border-radius: 5px; display: inline-block;
}
.status-badge.success { color: #00BFA5; background: rgba(0,191,165,0.12); }
.status-badge.error   { color: #F0616D; background: rgba(240,97,109,0.12); }

:deep(.traces-table.p-datatable .p-datatable-thead > tr > th) {
  background: rgba(255,255,255,0.02); border-bottom: 1px solid var(--border-subtle);
  color: var(--text-secondary); font-size: 11px; font-weight: 600; letter-spacing: 0.5px; text-transform: uppercase; padding: 12px 14px;
}
:deep(.traces-table.p-datatable .p-datatable-tbody > tr > td) {
  border-bottom: 1px solid var(--border-subtle); padding: 10px 14px; background: transparent; vertical-align: top;
}
:deep(.traces-table.p-datatable .p-datatable-tbody > tr:hover > td) { background: var(--bg-hover); }
</style>
