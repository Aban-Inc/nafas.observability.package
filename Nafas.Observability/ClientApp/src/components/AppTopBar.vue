<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Select from 'primevue/select'
import { useTimeRangeStore, TIME_RANGE_OPTIONS } from '@/stores/timeRange.store'
import { useLayoutStore } from '@/stores/layout.store'
import NotificationBell from '@/components/NotificationBell.vue'
import type { Language } from '@/models/option.model'

const { t } = useI18n()
const _time_range_store = useTimeRangeStore()
const _layout_store = useLayoutStore()

// No org pill, tenant switcher, or account menu here (unlike the SaaS
// platform this is derived from) -- no auth, no org, no tenant concept in
// this package at all. If/when a dashboard-auth hook is added (see
// dotnet/CLAUDE.md's roadmap), whatever identity it carries would surface
// here. Theme/direction/language controls moved from the SaaS platform's
// account-menu Settings dialog (deleted along with the rest of auth/
// settings) directly onto the bar, since there's no menu left to hang them
// off of -- these are the only "settings" this package still has.
//
// Only 'en'/'fa' offered here (not the full Language union option.model.ts
// still carries over from the SaaS platform) -- i18n/locales only actually
// has messages for those two; showing the rest would silently no-op via
// vue-i18n's fallback locale instead of really translating anything.
// computed, not a plain const -- the labels themselves need to re-translate
// when the user switches language (a plain const would freeze both labels
// in whatever language was active on mount, e.g. still show "Persian"
// after switching to Persian instead of "فارسی").
const LANGUAGE_OPTIONS = computed<{ label: string; value: Language }[]>(() => [
  { label: t('topbar.language.en'), value: 'en' },
  { label: t('topbar.language.fa'), value: 'fa' },
])

const isDark = computed(() => _layout_store.getTheme === 'dark')
const isRtl = computed(() => _layout_store.getDirection === 'rtl')
</script>

<template>
  <header class="topbar">
    <!-- Time range -->
    <div class="time-range-pill">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" style="color:var(--accent-bright)">
        <circle cx="12" cy="12" r="8.5" stroke="currentColor" stroke-width="1.8"/>
        <path d="M12 8v4l3 2" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
      </svg>
      <Select
        :model-value="_time_range_store.getRange"
        @update:model-value="_time_range_store.setRange"
        :options="TIME_RANGE_OPTIONS"
        option-label="label"
        option-value="value"
        class="time-range-select"
        :aria-label="t('topbar.timeRangeAriaLabel')"
      />
    </div>

    <div class="topbar-actions">
      <!-- Live indicator -->
      <div class="live-badge">
        <span class="live-dot"></span>
        <span>{{ t('topbar.live') }}</span>
      </div>

      <!-- Language -->
      <Select
        :model-value="_layout_store.getLanguage"
        @update:model-value="_layout_store.setLanguage"
        :options="LANGUAGE_OPTIONS"
        option-label="label"
        option-value="value"
        class="language-select"
        :aria-label="t('topbar.language.ariaLabel')"
      />

      <!-- Direction (LTR/RTL) -->
      <button
        type="button"
        class="icon-toggle"
        :title="isRtl ? t('topbar.direction.toggleToLtr') : t('topbar.direction.toggleToRtl')"
        @click="_layout_store.toggleDirection()"
      >
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none">
          <path d="M4 7h11M4 7l3-3M4 7l3 3" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>
          <path d="M20 17H9M20 17l-3-3M20 17l-3 3" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </button>

      <!-- Theme (light/dark) -->
      <button
        type="button"
        class="icon-toggle"
        :title="isDark ? t('topbar.theme.toggleToLight') : t('topbar.theme.toggleToDark')"
        @click="_layout_store.toggleTheme()"
      >
        <svg v-if="isDark" width="16" height="16" viewBox="0 0 24 24" fill="none">
          <circle cx="12" cy="12" r="4.5" stroke="currentColor" stroke-width="1.7"/>
          <path d="M12 2v2.5M12 19.5V22M4.2 4.2l1.8 1.8M18 18l1.8 1.8M2 12h2.5M19.5 12H22M4.2 19.8L6 18M18 6l1.8-1.8" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/>
        </svg>
        <svg v-else width="16" height="16" viewBox="0 0 24 24" fill="none">
          <path d="M20 14.5A8.5 8.5 0 1110 3.2 6.8 6.8 0 0020 14.5z" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"/>
        </svg>
      </button>

      <!-- Alert notifications (Phase 1 of the alerting roadmap) -->
      <NotificationBell />
    </div>
  </header>
</template>

<style scoped>
.topbar {
  height: 60px;
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-subtle);
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 0 22px;
  background: color-mix(in srgb, var(--bg-base) 70%, transparent);
  backdrop-filter: blur(12px);
  z-index: 3;
}

.topbar-actions {
  margin-inline-start: auto;
  display: flex;
  align-items: center;
  gap: 10px;
}

.time-range-pill {
  display: flex;
  align-items: center;
  gap: 8px;
}

.time-range-select {
  min-width: 130px;
}

.language-select {
  min-width: 96px;
  /* Matches .icon-toggle's own 36px (the direction/theme buttons right next
     to it) -- PrimeVue's default Select height doesn't line up with a plain
     36x36 icon button on its own. */
  height: 36px;
}
.language-select :deep(.p-select-label) {
  display: flex;
  align-items: center;
}

.icon-toggle {
  width: 36px;
  height: 36px;
  border-radius: 10px;
  border: 1px solid var(--border-medium);
  background: var(--bg-hover);
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  flex-shrink: 0;
}
.icon-toggle:hover { background: var(--bg-card); color: var(--text-primary); }

.live-badge {
  display: flex;
  align-items: center;
  gap: 7px;
  padding: 7px 11px;
  background: var(--accent-dim);
  border: 1px solid rgba(0,191,165,0.22);
  border-radius: 9px;
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-bright);
}
.live-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--accent);
  animation: nafasBlink 1.6s ease-in-out infinite;
}
</style>
