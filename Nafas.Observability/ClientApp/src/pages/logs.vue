<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import Select from 'primevue/select'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Skeleton from 'primevue/skeleton'
import LogService from '@/services/log.service'
import { LOG_LEVEL_VALUES, LOG_LEVEL_STYLE } from '@/models/log.dict'
import type { LogEntryViewModel } from '@/models/dashboard.vm'
import { useApiError } from '@/composables/useApiError'

const { t } = useI18n()
const _log_service: LogService = new LogService();
const { notifyError } = useApiError()

const LEVEL_LABEL_KEYS: Record<typeof LOG_LEVEL_VALUES[number], string> = {
  ALL: 'logs.levels.all',
  ERROR: 'logs.levels.error',
  WARN: 'logs.levels.warn',
  INFO: 'logs.levels.info',
  DEBUG: 'logs.levels.debug',
}
const levelOptions = computed(() => LOG_LEVEL_VALUES.map(value => ({ label: t(LEVEL_LABEL_KEYS[value]), value })))
const levelStyle = LOG_LEVEL_STYLE

const levelFilter = ref('ALL')
const serviceFilter = ref('')
const searchQuery = ref('')

const searchLoading = ref(false)
const searchError = ref(false)
const searchResults = ref<LogEntryViewModel[]>([])
const totalCount = ref(0)
const page = ref(1)
const pageSize = ref(50)

async function fetchSearchResults() {
  searchLoading.value = true
  searchError.value = false
  const now = new Date()
  const oneDayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000)
  const from = encodeURIComponent(oneDayAgo.toISOString())
  const to = encodeURIComponent(now.toISOString())
  try {
    const response = await _log_service.SearchLogs(
      from,
      to,
      levelFilter.value !== 'ALL' ? levelFilter.value : undefined,
      serviceFilter.value || undefined,
      searchQuery.value || undefined,
      page.value,
      pageSize.value
    )
    searchResults.value = response?.data ?? []
    totalCount.value = response?.totalCount ?? 0
  } catch (err) {
    console.error('Failed to search logs', err)
    searchResults.value = []
    totalCount.value = 0
    searchError.value = true
    notifyError('Log Search')
  } finally {
    searchLoading.value = false
  }
}

function onPage(event: { page: number; rows: number }) {
  page.value = event.page + 1
  pageSize.value = event.rows
  fetchSearchResults()
}

let searchDebounce: ReturnType<typeof setTimeout> | undefined
watch([searchQuery, serviceFilter], () => {
  clearTimeout(searchDebounce)
  searchDebounce = setTimeout(() => {
    page.value = 1
    fetchSearchResults()
  }, 400)
})

watch(levelFilter, () => {
  page.value = 1
  fetchSearchResults()
})

function clearFilters() {
  levelFilter.value = 'ALL'
  serviceFilter.value = ''
  searchQuery.value = ''
}

interface LogEntry {
  id: number
  timestamp: string
  level: 'ERROR' | 'WARN' | 'INFO' | 'DEBUG'
  service: string
  traceId: string
  message: string
  attributes: Record<string, string>
}

const KNOWN_LEVELS: LogEntry['level'][] = ['ERROR', 'WARN', 'INFO', 'DEBUG']

// The search endpoint has no LogAttributes column, so attributes is always empty here.
const displayedLogs = computed<LogEntry[]>(() => {
  return searchResults.value.map((entry, idx) => {
    const severity = entry.severityText.toUpperCase()
    const level = (KNOWN_LEVELS as string[]).includes(severity) ? (severity as LogEntry['level']) : 'INFO'

    return {
      id: (page.value - 1) * pageSize.value + idx,
      timestamp: entry.timestamp,
      level,
      service: entry.serviceName,
      traceId: entry.traceId,
      message: entry.body,
      attributes: {},
    }
  })
})

onMounted(() => fetchSearchResults())

const expandedRows = ref<Record<number, boolean>>({})
function toggleRow(id: number) {
  expandedRows.value[id] = !expandedRows.value[id]
}

async function copyToClipboard(text: string) {
  await navigator.clipboard.writeText(text)
}
</script>

<template>
  <div class="logs-page">

    <!-- Page header -->
    <div class="page-hdr">
      <div>
        <h1 class="page-title">{{ t('logs.title') }}</h1>
        <p class="page-sub">{{ t('logs.subtitle') }} · <code>us-east-1</code></p>
      </div>
      <div class="hdr-actions">
        <Button :label="t('logs.filter')" severity="secondary" outlined size="small" />
        <Button :label="t('logs.export')" severity="secondary" outlined size="small" />
      </div>
    </div>

    <!-- Toolbar -->
    <div class="toolbar card">
      <Select
        v-model="levelFilter"
        :options="levelOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('logs.levels.all')"
        class="filter-select"
      />
      <InputText
        v-model="serviceFilter"
        :placeholder="t('logs.filterByService')"
        class="filter-select"
      />
      <InputText
        v-model="searchQuery"
        :placeholder="t('logs.searchPlaceholder')"
        class="filter-input"
      />
      <div style="margin-inline-start:auto;">
        <Button :label="t('logs.clearFilters')" severity="secondary" outlined size="small" @click="clearFilters()" />
      </div>
    </div>

    <!-- Stats bar -->
    <div class="stats-bar">
      <div class="stat-chip">
        <span class="stat-label">{{ t('logs.stats.total') }}</span>
        <span class="stat-value font-mono">{{ totalCount }}</span>
      </div>
      <div class="stat-chip">
        <span class="stat-label">{{ t('logs.stats.errors') }}</span>
        <span class="stat-value font-mono" style="color:#F0616D;">
          {{ displayedLogs.filter(l => l.level === 'ERROR').length }}
        </span>
      </div>
      <div class="stat-chip">
        <span class="stat-label">{{ t('logs.stats.warnings') }}</span>
        <span class="stat-value font-mono" style="color:#F59E0B;">
          {{ displayedLogs.filter(l => l.level === 'WARN').length }}
        </span>
      </div>
    </div>

    <!-- Log Table -->
    <div class="card table-card">
      <DataTable
        :value="displayedLogs"
        :striped-rows="false"
        row-hover
        scrollable
        scroll-height="flex"
        class="logs-table"
        :loading="searchLoading"
        lazy
        paginator
        :rows="pageSize"
        :total-records="totalCount"
        @page="onPage"
        :pt="{
          root: { class: 'logs-dt' },
          thead: { class: 'logs-thead' },
        }"
      >
        <template #loading>
          <div style="display:flex;flex-direction:column;gap:8px;padding:14px;">
            <Skeleton v-for="i in 8" :key="i" height="30px" />
          </div>
        </template>
        <template #empty>
          <div class="table-empty">{{ searchError ? t('logs.loadFailed') : t('logs.noneFound') }}</div>
        </template>
        <Column field="timestamp" :header="t('logs.columns.timestamp')" style="width:160px">
          <template #body="{ data }">
            <span class="font-mono cell-mono">{{ data.timestamp }}</span>
          </template>
        </Column>

        <Column field="level" :header="t('logs.columns.level')" style="width:80px">
          <template #body="{ data }">
            <span
              class="level-badge font-mono"
              :style="{ color: levelStyle[data.level]?.color, background: levelStyle[data.level]?.bg }"
            >{{ data.level }}</span>
          </template>
        </Column>

        <Column field="service" :header="t('logs.columns.service')" style="width:140px">
          <template #body="{ data }">
            <span class="font-mono cell-service">{{ data.service }}</span>
          </template>
        </Column>

        <Column field="traceId" :header="t('logs.columns.traceId')" style="width:120px">
          <template #body="{ data }">
            <span class="font-mono cell-mono" style="color:var(--text-muted)">{{ data.traceId }}</span>
          </template>
        </Column>

        <Column field="message" :header="t('logs.columns.message')">
          <template #body="{ data }">
            <div>
              <div class="cell-msg">{{ data.message }}</div>
              <!-- Row expansion -->
              <button class="expand-btn" @click.stop="toggleRow(data.id)">
                {{ expandedRows[data.id] ? t('logs.hideDetails') : t('logs.showDetails') }}
              </button>
              <transition name="expand">
                <div v-if="expandedRows[data.id]" class="row-detail">
                  <div class="detail-section">
                    <div class="detail-title">{{ t('logs.fullMessage') }}</div>
                    <p class="detail-msg font-mono">{{ data.message }}</p>
                  </div>
                  <div class="detail-section">
                    <div class="detail-title">{{ t('logs.attributes') }}</div>
                    <div class="attr-grid">
                      <template v-for="(val, key) in data.attributes" :key="key">
                        <span class="attr-key font-mono">{{ key }}</span>
                        <span class="attr-val font-mono">{{ val }}</span>
                      </template>
                    </div>
                  </div>
                  <button class="copy-btn" @click="copyToClipboard(data.traceId)">
                    {{ t('logs.copyTraceId', { traceId: data.traceId }) }}
                  </button>
                </div>
              </transition>
            </div>
          </template>
        </Column>
      </DataTable>
    </div>

  </div>
</template>

<style scoped>
.logs-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  gap: 16px;
}

.page-hdr {
  flex-shrink: 0;
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
}
.page-title { margin: 0; font-size: 23px; font-weight: 700; letter-spacing: -0.5px; color: var(--text-primary); }
.page-sub   { margin: 5px 0 0; font-size: 13px; color: var(--text-secondary); }
.page-sub code { font-family: 'IBM Plex Mono', monospace; color: var(--text-secondary); }

.hdr-actions { display: flex; align-items: center; gap: 9px; }

.card {
  background: var(--bg-card);
  border: 1px solid var(--border-subtle);
  border-radius: 14px;
  backdrop-filter: blur(10px);
  box-shadow: inset 0 1px 0 rgba(255,255,255,0.04);
}

.toolbar {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
  flex-wrap: wrap;
}
.filter-select { min-width: 140px; }
.filter-input  { flex: 1; min-width: 200px; }

.stats-bar {
  flex-shrink: 0;
  display: flex;
  gap: 12px;
}
.stat-chip {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--bg-card);
  border: 1px solid var(--border-subtle);
  border-radius: 10px;
  padding: 10px 16px;
  backdrop-filter: blur(10px);
}
.stat-label { font-size: 12px; color: var(--text-secondary); }
.stat-value { font-size: 15px; font-weight: 700; color: var(--text-primary); }

.table-card { flex: 1; min-height: 0; padding: 0; overflow: hidden; }

.table-empty {
  padding: 24px;
  text-align: center;
  font-size: 12px;
  font-family: 'IBM Plex Mono', monospace;
  color: rgba(255, 255, 255, 0.2);
}

/* DataTable overrides */
:deep(.logs-dt) {
  background: transparent;
  border: none;
  width: 100%;
}
:deep(.logs-dt .p-datatable-header) { background: transparent; border: none; }
:deep(.logs-dt .p-datatable-thead > tr > th) {
  background: rgba(255,255,255,0.02);
  border-bottom: 1px solid var(--border-subtle);
  color: var(--text-secondary);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.5px;
  text-transform: uppercase;
  padding: 12px 14px;
}
:deep(.logs-dt .p-datatable-tbody > tr > td) {
  border-bottom: 1px solid var(--border-subtle);
  padding: 10px 14px;
  vertical-align: top;
  font-size: 13px;
  color: var(--text-primary);
  background: transparent;
}
:deep(.logs-dt .p-datatable-tbody > tr:hover > td) { background: var(--bg-hover); }

.font-mono { font-family: 'IBM Plex Mono', monospace; }
.cell-mono    { font-size: 11.5px; color: var(--text-secondary); }
.cell-service { font-size: 12px; color: var(--accent-bright); }
.cell-msg     { font-size: 13px; color: var(--text-primary); }

.level-badge {
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.4px;
  padding: 2px 7px;
  border-radius: 5px;
  display: inline-block;
}

.expand-btn {
  margin-top: 4px;
  background: none;
  border: none;
  color: var(--accent-bright);
  font-size: 10.5px;
  cursor: pointer;
  padding: 0;
  font-family: 'IBM Plex Mono', monospace;
}

.row-detail {
  margin-top: 10px;
  padding: 14px;
  background: rgba(255,255,255,0.02);
  border: 1px solid var(--border-subtle);
  border-radius: 9px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.detail-section { display: flex; flex-direction: column; gap: 6px; }
.detail-title   { font-size: 10px; font-weight: 600; letter-spacing: 0.8px; text-transform: uppercase; color: var(--text-muted); }
.detail-msg     { margin: 0; font-size: 12px; color: var(--text-primary); line-height: 1.5; }

.attr-grid {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 16px;
}
.attr-key { font-size: 11px; color: var(--text-muted); }
.attr-val { font-size: 11px; color: var(--accent-bright); }

.copy-btn {
  align-self: flex-start;
  background: var(--accent-dim);
  border: 1px solid rgba(0,191,165,0.22);
  border-radius: 7px;
  color: var(--accent-bright);
  font-size: 11px;
  font-family: 'IBM Plex Mono', monospace;
  padding: 5px 10px;
  cursor: pointer;
  transition: background 0.15s;
}
.copy-btn:hover { background: rgba(0,191,165,0.18); }

.expand-enter-active, .expand-leave-active { transition: opacity 0.15s, transform 0.15s; }
.expand-enter-from, .expand-leave-to { opacity: 0; transform: translateY(-4px); }
</style>
