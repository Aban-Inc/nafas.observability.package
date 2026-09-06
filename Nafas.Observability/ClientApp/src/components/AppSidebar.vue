<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { useDashboardStore } from '@/stores/dash.store'
import { getVersion } from '@/utils/serviceBaseUrl'

const _dashboard_store = useDashboardStore()
const { t } = useI18n()
const version = getVersion()

const navItems = computed(() => [
  { label: t('sidebar.nav.dashboard'), icon: 'M3 3h7v7H3V3zm11 0h7v7h-7V3zm0 11h7v7h-7v-7zM3 14h7v7H3v-7z', to: '/' },
  { label: t('sidebar.nav.logs'),      icon: 'M4 6h16M4 12h16M4 18h10', to: '/logs' },
  { label: t('sidebar.nav.metrics'),   icon: 'M4 17l4-5 3 3 4-7 5 6', to: '/metrics' },
  { label: t('sidebar.nav.traces'),    icon: 'M7 6h6a3 3 0 013 3v0M7 18h6a3 3 0 003-3v0', to: '/traces', extraPath: 'M5 6m-2.2 0a2.2 2.2 0 105 0 2.2 2.2 0 10-5 0M5 18m-2.2 0a2.2 2.2 0 105 0 2.2 2.2 0 10-5 0M19 12m-2.2 0a2.2 2.2 0 105 0 2.2 2.2 0 10-5 0' },
  // Profiles and Infrastructure removed from the product for now --
  // neither had a real query/display backend (see profiles.vue/
  // infrastructure.vue's own "honest placeholder" comments before this
  // change), and showing them as available nav items was misleading.
  { label: t('sidebar.nav.alerts'), icon: 'M12 2a6 6 0 00-6 6v3.586l-1.707 1.707A1 1 0 005 15h14a1 1 0 00.707-1.707L18 11.586V8a6 6 0 00-6-6zM9 18a3 3 0 006 0H9z', to: '/alerts' },
])

const route = useRoute()
const isActive = (to: string) => route.path === to
</script>

<template>
  <aside
    class="sidebar"
    :class="{ collapsed: _dashboard_store.getSidebarCollapsed }"
    :aria-label="t('sidebar.ariaLabel')"
  >
    <!-- Logo row -- typography only, no icon mark (see logo-row's own CSS
         comment on why) -->
    <div class="logo-row">
      <div class="logo-text">
        <span class="logo-name">Nafas</span>
        <span class="logo-sub">OBSERVABILITY</span>
      </div>
      <button class="collapse-btn" @click="_dashboard_store.toggleSideBar()" :aria-label="_dashboard_store.getSidebarCollapsed ? t('sidebar.expand') : t('sidebar.collapse')">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none">
          <path
            :d="_dashboard_store.sidebarCollapsed ? 'M9 18l6-6-6-6' : 'M15 18l-6-6 6-6'"
            stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"
          />
        </svg>
      </button>
    </div>

    <!-- Signals section -->
    <div class="nav-section-label">{{ t('sidebar.signals') }}</div>
    <nav class="nav-group">
      <RouterLink
        v-for="item in navItems"
        :key="item.to"
        :to="item.to"
        class="nav-item"
        :class="{ active: isActive(item.to) }"
      >
        <span class="nav-icon" v-tooltip.right="_dashboard_store.getSidebarCollapsed ? item.label : ''">
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none">
            <path :d="item.icon" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
            <path v-if="item.extraPath" :d="item.extraPath" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </span>
        <span class="nav-label">{{ item.label }}</span>
      </RouterLink>
    </nav>

    <!-- Footer -->
    <div class="sidebar-footer">
      <div class="version-row">
        <span>{{ version ? `v${version}` : t('common.loading') }}</span>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.sidebar {
  width: var(--sidebar-width);
  flex-shrink: 0;
  height: 100%;
  background: var(--bg-sidebar);
  border-inline-end: 1px solid var(--border-subtle);
  display: flex;
  flex-direction: column;
  padding: 18px 14px;
  transition: width 0.22s ease;
  overflow: hidden;
  position: relative;
  z-index: 2;
}

.sidebar.collapsed {
  width: var(--sidebar-collapsed-width);
  padding: 18px 8px;
}

/* Logo row */
.logo-row {
  display: flex;
  align-items: center;
  gap: 11px;
  padding: 6px 8px 18px 8px;
  position: relative;
}

.logo-text {
  display: flex;
  flex-direction: column;
  opacity: 1;
  width: auto;
  overflow: hidden;
  transition: opacity 0.15s ease, width 0.22s ease;
  white-space: nowrap;
}
.sidebar.collapsed .logo-text {
  opacity: 0;
  width: 0;
  pointer-events: none;
}

.logo-name  { font-size: 16px; font-weight: 700; letter-spacing: -0.3px; line-height: 1; color: var(--text-primary); }
.logo-sub   { font-size: 9px; font-weight: 600; letter-spacing: 1.4px; color: var(--text-label); margin-top: 3px; }

.collapse-btn {
  margin-inline-start: auto;
  background: none;
  border: 1px solid var(--border-subtle);
  border-radius: 7px;
  width: 26px;
  height: 26px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  color: var(--text-muted);
  flex-shrink: 0;
  transition: background 0.15s, border-color 0.15s;
  /* never hidden — excluded from opacity collapse rules */
  opacity: 1 !important;
  pointer-events: auto !important;
}
.collapse-btn:hover { background: var(--bg-hover); border-color: var(--border-medium); color: var(--text-primary); }

/* In collapsed state: logo-text collapses to width 0 above, leaving just
   collapse-btn -- centered instead of pinned to the end via margin-inline-start. */
.sidebar.collapsed .logo-row {
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 4px 0 12px;
}
.sidebar.collapsed .collapse-btn {
  margin-inline-start: 0;
}

/* Section labels */
.nav-section-label {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 1px;
  color: var(--text-label);
  padding: 14px 10px 8px;
  white-space: nowrap;
  opacity: 1;
  overflow: hidden;
  transition: opacity 0.15s ease;
}
.sidebar.collapsed .nav-section-label { opacity: 0; pointer-events: none; }
.ws-label { padding-top: 18px; }

/* Nav */
.nav-group {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 9px 11px;
  border-radius: 9px;
  font-size: 13.5px;
  font-weight: 500;
  color: var(--text-secondary);
  cursor: pointer;
  text-decoration: none;
  transition: background 0.15s, color 0.15s;
  white-space: nowrap;
  overflow: hidden;
}
.nav-item:hover { background: var(--bg-hover); color: var(--text-primary); }
.nav-item.active {
  font-weight: 600;
  color: var(--accent-bright);
  background: var(--accent-dim);
  box-shadow: inset 3px 0 0 var(--accent);
}

.sidebar.collapsed .nav-item { padding: 9px; gap: 0; justify-content: center; }
/* Active icon highlight works in both states: icon inherits color from nav-item.active */
.nav-item.active .nav-icon { color: var(--accent-bright); }
/* In collapsed state swap inset left-border indicator for a full background highlight */
.sidebar.collapsed .nav-item.active { box-shadow: none; }

.nav-icon { display: flex; align-items: center; justify-content: center; flex-shrink: 0; }

.nav-label {
  opacity: 1;
  width: auto;
  overflow: hidden;
  transition: opacity 0.15s ease, width 0.22s ease;
}
.sidebar.collapsed .nav-label { opacity: 0; width: 0; pointer-events: none; }

/* Footer */
.sidebar-footer {
  margin-top: auto;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.version-row {
  display: flex;
  align-items: center;
  padding: 0 4px;
  font-size: 10.5px;
  color: var(--text-label);
  font-family: 'IBM Plex Mono', monospace;
  opacity: 1;
  transition: opacity 0.15s ease;
}
.sidebar.collapsed .version-row { opacity: 0; pointer-events: none; height: 0; }
</style>
