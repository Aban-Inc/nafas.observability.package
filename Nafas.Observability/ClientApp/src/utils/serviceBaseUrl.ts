// Replaces the SaaS platform's multi-gateway resolveServiceBaseUrl() -- this
// package has exactly one backend: whatever ASP.NET Core app it's embedded
// in, via Nafas.Dashboard's own middleware-registered controllers. No
// gateway, no per-signal base URL.
//
// The one thing that IS still runtime-variable is the base PATH, because the
// consuming app chooses where to mount the dashboard (app.UseNafasDashboard
// ("/nafas") vs "/observability" vs anything else, same as Hangfire's own
// dashboard) -- and that choice isn't known at `npm run build` time, since
// the built output is a single static bundle shipped inside the DLL and
// reused by every consuming app. NafasDashboardExtensions.UseNafasDashboard
// injects `window.__NAFAS_BASE_PATH__` (and `__NAFAS_VERSION__`, see
// getVersion() below) into the index.html it serves.
declare global {
  interface Window {
    __NAFAS_BASE_PATH__?: string
    __NAFAS_VERSION__?: string
  }
}

// Falls back to '' (root) so `npm run dev` still works standalone against a
// dashboard mounted at the app root.
export function getBasePath(): string {
  return window.__NAFAS_BASE_PATH__ ?? ''
}

// The real installed package version (see UseNafasDashboard's own comment on
// where this actually comes from -- the assembly's own version, not a
// hardcoded display string). Falls back to '' so `npm run dev` doesn't show
// a fabricated version number when run standalone, outside the DLL.
export function getVersion(): string {
  return window.__NAFAS_VERSION__ ?? ''
}
