import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  // Relative asset URLs (./assets/...), not absolute (/assets/...) --
  // this bundle gets served from whatever path the consuming app mounts
  // the dashboard at (app.UseNafasDashboard("/nafas") etc.), decided at
  // runtime by that app, not at `npm run build` time. Relative paths
  // resolve correctly no matter where index.html itself was served from;
  // see src/utils/serviceBaseUrl.ts for how the *API* base path (a
  // separate concern) is communicated at runtime instead.
  base: './',
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '~': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
