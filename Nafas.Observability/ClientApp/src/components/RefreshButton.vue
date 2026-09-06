<script setup lang="ts">
import { useI18n } from 'vue-i18n'

defineProps<{
  loading?: boolean
}>()

const emit = defineEmits<{
  refresh: []
}>()

const { t } = useI18n()
</script>

<template>
  <button
    class="refresh-btn"
    type="button"
    :disabled="loading"
    :title="t('common.refresh')"
    :aria-label="t('common.refreshSection')"
    @click="emit('refresh')"
  >
    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" :class="{ spinning: loading }">
      <path d="M3 12a9 9 0 0115.5-6.36M21 12a9 9 0 01-15.5 6.36" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" />
      <path d="M18.5 3v5h-5M5.5 21v-5h5" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" />
    </svg>
  </button>
</template>

<style scoped>
.refresh-btn {
  width: 24px;
  height: 24px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 7px;
  border: 1px solid var(--border-medium);
  background: var(--bg-hover);
  color: var(--text-secondary);
  cursor: pointer;
  transition: background 0.15s, color 0.15s, border-color 0.15s;
}

.refresh-btn:hover:not(:disabled) {
  background: var(--bg-card);
  color: var(--accent-bright);
  border-color: rgba(255, 255, 255, 0.16);
}

.refresh-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.spinning {
  animation: nafas-refresh-spin 0.8s linear infinite;
}

@keyframes nafas-refresh-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>
