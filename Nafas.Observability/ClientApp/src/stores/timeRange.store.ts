import { computed } from 'vue'
import { defineStore } from 'pinia'
import { i18n } from '@/i18n'

// Governs the time window every dashboard widget queries -- previously each
// KPI/chart hardcoded its own window (1h for KPIs, 24h for charts) and the
// topbar's "Last 24H" pill did nothing at all. Persisted the same
// guarded-access way as layout.store.ts's theme/direction.
export type TimeRangeKey = '24h' | '7d' | '30d' | '180d' | '365d'

const RANGE_HOURS: Record<TimeRangeKey, number> = {
  '24h': 24,
  '7d': 24 * 7,
  '30d': 24 * 30,
  '180d': 24 * 180,
  '365d': 24 * 365,
}

const TIME_RANGE_KEYS: TimeRangeKey[] = ['24h', '7d', '30d', '180d', '365d']

// A computed, not a plain array -- AppTopBar's Select reads this directly in
// its template (auto-unwrapped there), so the labels have to update
// themselves on a language switch instead of being fixed at import time.
export const TIME_RANGE_OPTIONS = computed<{ label: string; value: TimeRangeKey }[]>(() =>
  TIME_RANGE_KEYS.map(value => ({ label: i18n.global.t(`dashboard.timeRange.${value}`), value }))
)

const VALID_KEYS = TIME_RANGE_KEYS as string[]
const STORAGE_KEY = 'nafas-time-range'

function readStoredRange(): TimeRangeKey {
  const stored = typeof localStorage !== 'undefined' ? localStorage.getItem(STORAGE_KEY) : null
  return VALID_KEYS.includes(stored ?? '') ? (stored as TimeRangeKey) : '24h'
}

export const useTimeRangeStore = defineStore('time-range-store', {
  state() {
    return {
      range: readStoredRange() as TimeRangeKey,
    }
  },
  actions: {
    setRange(range: TimeRangeKey): void {
      this.range = range
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(STORAGE_KEY, range)
      }
    },
  },
  getters: {
    getRange(state): TimeRangeKey {
      return state.range
    },
    getHours(state): number {
      return RANGE_HOURS[state.range]
    },
    getLabel(): string {
      return TIME_RANGE_OPTIONS.value.find(o => o.value === this.range)?.label ?? i18n.global.t('dashboard.timeRange.24h')
    },
  },
})
