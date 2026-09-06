<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Skeleton from 'primevue/skeleton'
import type { LatencyHeatmapPointViewModel } from '@/models/dashboard.vm'

const { t } = useI18n()

const props = defineProps<{
  data: LatencyHeatmapPointViewModel[] | null
  loading?: boolean
  error?: boolean
  // Matches whatever bucket_hours the request used -- >=24 means each column
  // is a day (or more), so labels should read as dates, not times, and with
  // long ranges (e.g. 365 daily buckets) most of them get skipped so the
  // axis stays legible instead of an unreadable wall of text.
  bucketHours?: number
}>()

const MAX_VISIBLE_LABELS = 14
const labelStride = computed(() => {
  const n = props.data?.length ?? 0
  return n > MAX_VISIBLE_LABELS ? Math.ceil(n / MAX_VISIBLE_LABELS) : 1
})

const ROWS = [
  { key: 'p50' as const, label: 'p50' },
  { key: 'p95' as const, label: 'p95' },
  { key: 'p99' as const, label: 'p99' },
]

// Each row (p50/p95/p99) is normalized against its own min/max, not a shared
// scale -- p99 values are typically an order of magnitude above p50, and a
// single shared scale would just render the p50 row as a flat dead color.
const rowRanges = computed(() => {
  if (!props.data || props.data.length === 0) return null
  const ranges: Record<string, { min: number; max: number }> = {}
  for (const row of ROWS) {
    const values = props.data.map(p => p[row.key])
    ranges[row.key] = { min: Math.min(...values), max: Math.max(...values) }
  }
  return ranges
})

// rgb(0,191,165) (--accent) at the low end -> rgb(240,97,109) (error red) at
// the high end, same two colors already used for "good"/"bad" elsewhere on
// this dashboard (see ErrorRateChart, .error-glow).
function cellColor(value: number, min: number, max: number): string {
  const t = max > min ? (value - min) / (max - min) : 0.3
  const r = Math.round(0 + (240 - 0) * t)
  const g = Math.round(191 + (97 - 191) * t)
  const b = Math.round(165 + (109 - 165) * t)
  return `rgba(${r}, ${g}, ${b}, ${0.18 + t * 0.62})`
}

function formatMs(value: number): string {
  return value >= 1000 ? `${(value / 1000).toFixed(1)}s` : `${Math.round(value)}`
}

function formatBucketLabel(timestamp: string): string {
  const d = new Date(timestamp)
  return (props.bucketHours ?? 1) >= 24
    ? d.toLocaleDateString([], { month: 'short', day: 'numeric' })
    : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}
</script>

<template>
  <div class="heatmap-wrap">
    <Skeleton v-if="loading" width="100%" height="100%" />
    <template v-else-if="data && data.length > 0 && rowRanges">
      <div class="heatmap-grid" :style="{ gridTemplateColumns: `44px repeat(${data.length}, minmax(0, 1fr))` }">
        <template v-for="row in ROWS" :key="row.key">
          <span class="row-label">{{ row.label }}</span>
          <span
            v-for="point in data"
            :key="`${row.key}-${point.timestamp}`"
            class="heatmap-cell"
            :style="{ background: cellColor(point[row.key], rowRanges[row.key].min, rowRanges[row.key].max) }"
            :title="`${row.label} · ${formatBucketLabel(point.timestamp)} · ${point[row.key].toFixed(1)}ms`"
          >{{ formatMs(point[row.key]) }}</span>
        </template>
        <span class="corner" />
        <span v-for="(point, i) in data" :key="`axis-${point.timestamp}`" class="col-label">{{ i % labelStride === 0 ? formatBucketLabel(point.timestamp) : '' }}</span>
      </div>
    </template>
    <div v-else class="heatmap-placeholder">
      <span>{{ error ? t('charts.latencyHeatmapFailed') : t('charts.noTraceData') }}</span>
    </div>
  </div>
</template>

<style scoped>
.heatmap-wrap {
  position: relative;
  width: 100%;
  min-height: 160px;
}

.heatmap-grid {
  display: grid;
  grid-auto-rows: 26px;
  gap: 3px;
  align-items: center;
}

.row-label {
  font-size: 10.5px;
  font-weight: 600;
  color: var(--text-muted);
  font-family: 'IBM Plex Mono', monospace;
  text-align: end;
  padding-inline-end: 6px;
}

.heatmap-cell {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  border-radius: 4px;
  font-size: 9.5px;
  font-weight: 600;
  font-family: 'IBM Plex Mono', monospace;
  color: var(--text-primary);
  cursor: default;
}

.corner {
  height: 100%;
}

.col-label {
  font-size: 9px;
  color: var(--text-muted);
  font-family: 'IBM Plex Mono', monospace;
  text-align: center;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.heatmap-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 160px;
  color: rgba(255, 255, 255, 0.2);
  font-size: 12px;
  font-family: 'IBM Plex Mono', monospace;
  border: 1px dashed rgba(255, 255, 255, 0.08);
  border-radius: 8px;
}
</style>
