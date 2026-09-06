import { watchEffect } from 'vue'
import { useLayoutStore } from '@/stores/layout.store'

// Replaces Nuxt's useHead({ htmlAttrs: { 'data-theme': ..., dir: ... } }),
// which the original app called identically from default.vue, auth.vue, and
// onboarding.vue. Applied once, app-wide (see App.vue) — every route is
// wrapped by exactly one layout at a time, so a single call site here
// covers all three without re-registering the same watcher per layout.
export function useThemeAttributes(): void {
  const _layout_store = useLayoutStore()

  watchEffect(() => {
    document.documentElement.setAttribute('data-theme', _layout_store.getTheme)
    document.documentElement.setAttribute('dir', _layout_store.getDirection)
  })
}
