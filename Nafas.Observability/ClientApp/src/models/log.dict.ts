// Values only -- labels are translated in logs.vue (i18n keys logs.levels.*)
// since this file has no component context to call useI18n() from.
export const LOG_LEVEL_VALUES = ['ALL', 'ERROR', 'WARN', 'INFO', 'DEBUG'] as const

export const LOG_LEVEL_STYLE: Record<string, { color: string; bg: string }> = {
  ERROR: { color: '#F0616D', bg: 'rgba(240,97,109,0.14)' },
  WARN: { color: '#F59E0B', bg: 'rgba(245,158,11,0.14)' },
  INFO: { color: '#00BFA5', bg: 'rgba(0,191,165,0.14)' },
  DEBUG: { color: '#7A808C', bg: 'rgba(122,128,140,0.14)' },
}
