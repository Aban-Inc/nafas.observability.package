<script setup lang="ts">
// Phase 1 of the alerting roadmap -- the in-app half of "closes the no-
// notification gap" (webhook/SMS/push are later phases). Connects to the
// SSE stream (useAlertStream) on mount and keeps a small in-memory backlog
// of recent incident open/resolved events for as long as the tab stays
// open -- no persistence, no unread state carried across a reload, same as
// the existing live-log stream's own scope. No tenant gate here (unlike
// the SaaS platform this is derived from) -- this package has exactly one
// implicit "tenant", so the stream connects unconditionally.
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAlertStream, type AlertStreamEvent } from '@/composables/useAlertStream'

const { t } = useI18n()

const MAX_EVENTS = 20

const events = ref<AlertStreamEvent[]>([])
const unreadCount = ref(0)
const menuOpen = ref(false)
const menuRef = ref<HTMLElement | null>(null)

let stream: ReturnType<typeof useAlertStream> | null = null

function handleEvent(event: AlertStreamEvent) {
  events.value.unshift(event)
  if (events.value.length > MAX_EVENTS) events.value.pop()
  if (!menuOpen.value) unreadCount.value++
}

onMounted(() => {
  stream = useAlertStream(handleEvent)
  stream.connect()
  document.addEventListener('mousedown', handleOutsideClick)
})

onUnmounted(() => {
  stream?.disconnect()
  document.removeEventListener('mousedown', handleOutsideClick)
})

function toggleMenu() {
  menuOpen.value = !menuOpen.value
  if (menuOpen.value) unreadCount.value = 0
}

function handleOutsideClick(event: MouseEvent) {
  if (menuOpen.value && menuRef.value && !menuRef.value.contains(event.target as Node)) {
    menuOpen.value = false
  }
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
}
</script>

<template>
  <div class="bell-wrap" ref="menuRef">
    <button type="button" class="bell-button" :title="t('notifications.title')" @click="toggleMenu()">
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none">
        <path d="M12 2a6 6 0 00-6 6v3.586l-1.707 1.707A1 1 0 005 15h14a1 1 0 00.707-1.707L18 11.586V8a6 6 0 00-6-6z" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>
        <path d="M9 18a3 3 0 006 0" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/>
      </svg>
      <span v-if="unreadCount > 0" class="bell-badge">{{ unreadCount > 9 ? '9+' : unreadCount }}</span>
    </button>

    <div v-if="menuOpen" class="bell-panel" role="menu">
      <div class="bell-panel-header">{{ t('notifications.title') }}</div>

      <div v-if="events.length === 0" class="bell-empty">{{ t('notifications.empty') }}</div>

      <div v-else class="bell-list">
        <div v-for="(event, index) in events" :key="`${event.ruleId}-${event.at}-${index}`" class="bell-item">
          <span class="bell-dot" :class="event.type === 'incident.open' ? 'bell-dot-open' : 'bell-dot-resolved'"></span>
          <div class="bell-item-body">
            <span class="bell-item-title">{{ event.ruleName }}</span>
            <span class="bell-item-detail">{{ event.detail ?? (event.type === 'incident.open' ? t('notifications.opened') : t('notifications.resolved')) }}</span>
          </div>
          <span class="bell-item-time">{{ formatTime(event.at) }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.bell-wrap { position: relative; }

.bell-button {
  position: relative;
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
}
.bell-button:hover { background: var(--bg-card); color: var(--text-primary); }

.bell-badge {
  position: absolute;
  top: -4px;
  inset-inline-end: -4px;
  min-width: 16px;
  height: 16px;
  padding: 0 3px;
  border-radius: 999px;
  background: var(--error, #E5484D);
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  line-height: 16px;
  text-align: center;
}

.bell-panel {
  position: absolute;
  top: calc(100% + 10px);
  inset-inline-end: 0;
  width: 320px;
  max-height: 380px;
  overflow-y: auto;
  background: var(--bg-solid);
  border: 1px solid var(--border-medium);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.28);
  padding: 8px;
  z-index: 20;
}

.bell-panel-header { padding: 6px 8px 10px; font-size: 12.5px; font-weight: 700; color: var(--text-primary); }
.bell-empty { padding: 16px 8px; font-size: 12.5px; color: var(--text-muted); text-align: center; }

.bell-list { display: flex; flex-direction: column; gap: 2px; }
.bell-item {
  display: flex;
  align-items: flex-start;
  gap: 9px;
  padding: 9px 8px;
  border-radius: 8px;
}
.bell-item:hover { background: var(--bg-hover); }

.bell-dot { width: 8px; height: 8px; border-radius: 50%; margin-top: 5px; flex-shrink: 0; }
.bell-dot-open { background: var(--error, #E5484D); }
.bell-dot-resolved { background: #1F8A5F; }

.bell-item-body { display: flex; flex-direction: column; gap: 1px; flex: 1; min-width: 0; }
.bell-item-title { font-size: 12.5px; font-weight: 600; color: var(--text-primary); }
.bell-item-detail { font-size: 11.5px; color: var(--text-secondary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.bell-item-time { font-size: 11px; color: var(--text-muted); flex-shrink: 0; }
</style>
