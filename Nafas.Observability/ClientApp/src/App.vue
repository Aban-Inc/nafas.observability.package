<script setup lang="ts">
import Toast from 'primevue/toast'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useThemeAttributes } from '@/composables/useThemeAttributes'

// Only one layout exists in this package -- no auth/onboarding/admin
// layouts, unlike the SaaS platform this is derived from (this is an
// embedded dashboard, not a multi-page product).
useThemeAttributes()
</script>

<template>
  <Toast position="top-right" />
  <DefaultLayout>
    <RouterView />
  </DefaultLayout>
</template>
