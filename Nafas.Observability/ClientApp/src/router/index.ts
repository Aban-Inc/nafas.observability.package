import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { getBasePath } from '@/utils/serviceBaseUrl'

// No route guards here (unlike the SaaS platform this is derived from) --
// no auth, no onboarding, no tenant resolution. Every route is reachable
// directly; if/when a dashboard-auth hook is added (see dotnet/CLAUDE.md's
// roadmap), it's enforced server-side (like Hangfire's
// IDashboardAuthorizationFilter), not here.
const routes: RouteRecordRaw[] = [
  { path: '/', name: 'index', component: () => import('@/pages/index.vue') },
  { path: '/logs', name: 'logs', component: () => import('@/pages/logs.vue') },
  { path: '/metrics', name: 'metrics', component: () => import('@/pages/metrics.vue') },
  { path: '/traces', name: 'traces', component: () => import('@/pages/traces.vue') },
  { path: '/alerts', name: 'alerts', component: () => import('@/pages/alerts.vue') },
]

// createWebHistory's base matches wherever the consuming app mounted this
// dashboard (app.UseNafasDashboard("/nafas") etc.) -- see
// serviceBaseUrl.ts's getBasePath() for how that's communicated at runtime.
export const router = createRouter({
  history: createWebHistory(getBasePath()),
  routes,
})

export default router
