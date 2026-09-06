# CLAUDE.md — Nafas.Observability

## What is this?

A self-hosted, embeddable observability dashboard for ASP.NET Core,
distributed as a single NuGet package -- Hangfire's own distribution model:

```csharp
services.AddNafasServer(options => { ... }); // storage + retention + ingestion + alerting
app.UseNafasDashboard("/nafas");             // mounts the dashboard UI + API
```

Why this exists: it's the free-tier, embeddable counterpart to Aban-Inc's
full SaaS observability platform (Redpanda/ClickHouse/Redis/Vector/YARP),
built for deployments that can't assume reliable connectivity to that stack
or want zero extra infrastructure. This package trades the SaaS stack's
scale for **zero external dependencies**: one process, one DLL, a local
SQLite file by default, nothing to deploy separately, nothing that calls out
to any external host at runtime -- not a CDN, not a cloud API, not even
Google Fonts (see the dashboard UI notes below).

**No new NuGet packages without stating why and getting explicit approval
first** -- every csproj in this repo documents the reasoning for each
package already there; match that standard for any new one.

## Solution structure

```
nafas.observability.sln
├── README.md / README.fa.md      -- repo-root docs (also what renders on the
│                                    NuGet package page, via PackageReadmeFile
│                                    pointing at ../README.md from the csproj)
├── Nafas.Observability/          -- the package itself (netstandard2.1)
│   ├── NafasServiceCollectionExtensions.cs  -- AddNafasServer(...)
│   ├── NafasDashboardExtensions.cs          -- UseNafasDashboard(path)
│   ├── NafasDashboardEndpoints.cs           -- hand-rolled api/* router
│   ├── NafasServerOptions.cs                -- the options object
│   ├── ConnectionStringResolver.cs
│   ├── DatabaseProvider.cs                  -- Sqlite | SqlServer
│   ├── NafasLiveFeed.cs                     -- in-process pub/sub for SSE
│   ├── NafasRetentionHostedService.cs
│   ├── NafasAlertWebhookSender.cs
│   ├── NafasAlertEvaluationHostedService.cs
│   ├── NafasSchemaInitializationHostedService.cs
│   ├── Storage/            -- schema provisioning + read queries + write batches
│   │   ├── INafasSchemaProvisioner.cs, Sqlite*/SqlServer* impls
│   │   ├── INafasQueryStore.cs, Sqlite*/SqlServer* impls   (dashboard reads + alert state)
│   │   ├── INafasIngestionWriter.cs, Sqlite*/SqlServer* impls (ingestion writes)
│   │   ├── QueryModels.cs          -- read-side DTOs (mirror ClientApp's *.vm.ts)
│   │   └── IngestionRecords.cs     -- write-side DTOs
│   ├── Ingestion/          -- real telemetry capture (see its own section below)
│   │   ├── NafasIngestionQueue.cs
│   │   ├── NafasLoggerProvider.cs                  -- ILogger -> nafas_logs
│   │   ├── NafasActivityIngestionHostedService.cs  -- Activity -> nafas_traces
│   │   ├── NafasMeterIngestionHostedService.cs     -- Meter -> nafas_metrics
│   │   ├── NafasResourceMetricsHostedService.cs    -- cpu/memory sampler
│   │   └── NafasIngestionWriterHostedService.cs    -- drains the queue, batches writes
│   ├── ClientApp/          -- the dashboard UI: Vue 3 + Vite SPA (see below)
│   ├── wwwroot/            -- ClientApp's BUILT output, embedded into the DLL
│   ├── LICENSE.md / LICENSE.fa.md
│   └── icon.png             -- NuGet package icon
└── Nafas.Dashboard.TestHost/  -- minimal ASP.NET Core app for manual verification
```

## The dashboard UI (ClientApp/)

Forked from Aban-Inc's SaaS platform's own Vue SPA and stripped of
auth/tenant/admin/capacitor/billing -- **not** a lightweight Razor/jQuery
rebuild (an earlier plan to do that was explicitly reversed). Vue 3 + Vite +
PrimeVue 4 + Pinia + vue-i18n, standalone (no shared workspace, no shared
package with anything else).

- **Build/embed workflow** (manual, no MSBuild-triggered step yet):
  ```bash
  cd Nafas.Observability/ClientApp
  npm install                      # first time only
  npm run build                    # -> ClientApp/dist/
  # then copy dist/* into ../wwwroot/ (replace, don't merge stale files)
  ```
  `wwwroot/**` is embedded into the DLL via `<EmbeddedResource Include="wwwroot\**" />`
  in the csproj -- **do not** give it a custom `LogicalName`; MSBuild's default
  dot-joined naming (`Nafas.Observability.wwwroot.assets.index-*.js`) is what
  `EmbeddedFileProvider(assembly, "Nafas.Observability.wwwroot")` expects. A
  slash-separated LogicalName was tried once and built fine but 500'd on
  every request at runtime -- don't reintroduce it.
- **Runtime-configurable mount path**: `vite.config.ts` sets `base: './'`
  (relative asset URLs), and `NafasDashboardExtensions.UseNafasDashboard`
  injects `<base href="{path}/">` as the *first* thing after `<head>` in the
  served `index.html`, plus `window.__NAFAS_BASE_PATH__` /
  `window.__NAFAS_VERSION__` (read by `src/utils/serviceBaseUrl.ts`). Order
  matters: appending `<base>` before `</head>` instead does nothing, since it
  only affects tags the parser reaches *after* it, and Vite's own
  `<script>/<link>` tags are already earlier in the document.
- **Zero external assets, on purpose**: nothing in this SPA ever fetches
  from Google Fonts, a CDN, or any other external host -- consistent with the
  whole package's zero-dependency premise. The Vazir Persian font (SIL OFL
  1.1, `rastikerdar/vazir-font`) is bundled locally under
  `src/assets/fonts/vazir/*.woff2` (in `src/assets/`, not `public/`, so Vite
  fingerprints/hashes it correctly for base-relative resolution) and covers
  both Persian and Latin glyphs, so it's the only font this UI depends on.
  Before adding any new font, icon set, or script tag here, confirm it's
  either bundled locally or loaded from the npm package's own bundled files
  (e.g. `primeicons` ships its own font files, no CDN) -- never a `<link>` or
  `@import` to an external host.
- **Defaults**: RTL + dark (`src/stores/layout.store.ts`), Persian as the
  default language (`src/i18n/index.ts`) -- Persian/RTL is this package's
  primary audience, not an afterthought toggle. en/fa, dark/light, and
  LTR/RTL are all switchable from the top bar (`AppTopBar.vue`).
- **api/\* contract**: every response is wrapped
  `{ content, message, success, errors }` (see
  `NafasDashboardEndpoints.WriteEnvelopeAsync`) -- `ClientApp`'s services
  unwrap `.content`. DTOs in `Storage/QueryModels.cs` are written to match
  `ClientApp/src/models/*.vm.ts` field-for-field (camelCase falls out of
  System.Text.Json's default for free) -- changing one without the other
  breaks the UI silently (wrong/missing fields, not a compile error).

## Why a hand-rolled router (NafasDashboardEndpoints.cs)

ASP.NET Core's `IEndpointRouteBuilder`/`MapGet` has had **no standalone
NuGet package since Endpoint Routing shipped in 3.0** -- it's
`FrameworkReference`-only from there on, and `FrameworkReference` isn't
available to a netstandard2.x class library at all. Confirmed the hard way:
`Microsoft.AspNetCore.Routing` 2.2.2 is the last version with a real
package, and it predates Endpoint Routing entirely. Hangfire hits this exact
constraint and solves it the same way: a small hand-rolled route table, not
ASP.NET Core's own routing system. Don't try to reintroduce
`Microsoft.AspNetCore.Routing`/`MapGet` here -- it doesn't work on this
target framework, full stop.

## The two-database-provider pattern (Storage/)

`DatabaseProvider.Sqlite` (default, zero-config) or `DatabaseProvider.SqlServer`.
Every `Storage/` interface (`INafasSchemaProvisioner`, `INafasQueryStore`,
`INafasIngestionWriter`) has one implementation per provider, selected once
in `NafasServiceCollectionExtensions.AddNafasServer` at startup -- genuine
runtime polymorphism, so a real interface per concern is correct here, not
over-engineering. Adding a third provider means adding a third
implementation of all three interfaces plus a `DatabaseProvider` enum
member -- Postgres/MySQL/ClickHouse are deliberately not options here;
they're the SaaS platform's own Pro-tier story, not this package's.

Tables (`nafas_logs`, `nafas_metrics`, `nafas_traces`, `nafas_alert_rules`,
`nafas_alert_incidents`) are a simplified, relational reinterpretation of
the SaaS platform's own OTel-shaped ClickHouse schema -- same core columns
(Timestamp, ServiceName, SeverityText/Body for logs, SpanName/Duration/
StatusCode for traces, MetricName/Value for metrics), but attribute maps
collapse to a single JSON text column (neither SQLite nor SQL Server has a
native Map type) and the several ClickHouse metric-type tables collapse
into one `nafas_metrics` with a `MetricType` discriminator. SQL Server's
optional `Schema` option gets validated against `^[A-Za-z_][A-Za-z0-9_]*$`
before ever being interpolated into DDL/DML -- it's the only way to
parameterize a SQL Server identifier, so don't relax that pattern without
understanding why it's there.

**Metric name/unit conventions** (`SqliteQueryStore`/`SqlServerQueryStore`'s
`MetricNames`, populated by `Ingestion/`):
- `cpu_usage`, `memory_usage` -- percent, 0-100.
- `request_rate` -- requests/sec.
- `http_server_duration` -- **seconds**, not milliseconds (OTel's own
  duration convention). The dashboard multiplies by 1000 for display
  (`ClientApp/src/pages/metrics.vue`'s `formattedP99Latency`) -- getting this
  wrong once already produced a real "176452 ms" bug from a seed script.
- `nafas_traces.DurationNanos` is stored in **nanoseconds**. KPI/chart
  aggregation code (`GetLatencyHeatmapAsync`, `TracesOverviewKpi`) converts to
  ms; `SearchTracesAsync`'s raw value is deliberately left unconverted
  because `ClientApp/src/pages/traces.vue` divides it itself -- don't "fix"
  that inconsistency without updating the frontend to match.

## Real-time architecture (NafasLiveFeed.cs) -- the one rule that must never break

**Live/"recent" dashboard data must never be re-read from the database on a
timer.** It gets pushed once, at write time, into `NafasLiveFeed` (an
in-process `System.Threading.Channels`-based pub/sub -- the in-process
equivalent of the SaaS platform's own Redis Pub/Sub), and every open SSE
connection (`api/logs/stream`, `api/alerts/stream`) receives it immediately.
The database is for historical queries only (search, charts over a date
range, KPIs). This is enforced on the read side too: KPI endpoints cache 30s,
chart endpoints cache 60s (`Microsoft.Extensions.Caching.Memory`, wrapped as
`NafasMemoryCache` to avoid a DI collision with the consuming app's own
`IMemoryCache`), search/list/stream endpoints are never cached.

## Ingestion (Ingestion/) -- what actually produces the data

This is what makes `AddNafasServer()` alone -- no other code in the
consuming app -- produce real logs/metrics/traces:

- **Logs**: `NafasLoggerProvider` is registered as `ILoggerProvider`, so
  ASP.NET Core's own `ILoggerFactory` calls it for every `ILogger<T>` line
  the app or its dependencies write -- no separate opt-in. Severity mapped
  to OTel's `SeverityNumber` ranges; correlates to `Activity.Current`'s
  TraceId/SpanId when a log happens inside a request.
- **Traces**: `NafasActivityIngestionHostedService` subscribes an
  `ActivityListener` to **every** `ActivitySource` in the process
  (`ShouldListenTo = _ => true`) -- deliberate, since there's no way to know
  an app's `ActivitySource` names in advance, and it's what makes ASP.NET
  Core's own built-in per-request `Activity`
  (`Microsoft.AspNetCore.Hosting.HttpRequestIn`) show up with zero
  instrumentation code. If this ever proves too noisy (third-party library
  spans nobody wants), a name-filter option on `NafasServerOptions` is the
  natural fix -- not built, since nobody's asked for it yet.
- **Metrics**: `NafasMeterIngestionHostedService` subscribes a
  `MeterListener` to every `Meter`/instrument the same way. ASP.NET Core 8+
  automatically records `http.server.request.duration` (seconds) on its own
  built-in Meter for every request with zero instrumentation -- this is
  renamed to `http_server_duration` on the way in (matching the `MetricNames`
  convention above) and also counted to derive `request_rate` every 15s.
  `NafasResourceMetricsHostedService` separately polls `Process`/`GC` every
  15s for `cpu_usage`/`memory_usage` -- there's no built-in Meter for those,
  so it's a small dedicated native sampler instead (see that file's own
  comment on why `GC.GetGCMemoryInfo()` needs reflection on netstandard2.1).
- **The queue** (`NafasIngestionQueue`): every listener above only ever
  enqueues (bounded channels, `DropWrite` on overflow) -- **never** writes to
  the database directly, and the enqueue call itself never blocks. This
  matters because these listeners run directly in the consuming app's own
  hot paths (every log call, every span, every measurement); losing
  telemetry under sustained overload is acceptable, slowing down or crashing
  the host app is not.
- **The writer** (`NafasIngestionWriterHostedService`): the only thing that
  drains the queue and actually talks to the database, batching (up to 500
  records / 1s window) via `INafasIngestionWriter`. Also the only thing that
  publishes newly-ingested logs to `NafasLiveFeed`'s `"logs"` channel, in the
  exact OTLP-shaped JSON `ClientApp/src/composables/useLogStream.ts` expects
  (`resourceLogs[].scopeLogs[].logRecords[]`) -- match that shape exactly if
  you ever touch this, the frontend does not parse anything else.

## Alert evaluation (NafasAlertEvaluationHostedService.cs)

Turns a stored `nafas_alert_rules` row into a real `nafas_alert_incidents`
row -- polls every 30s (rule windows are minutes-granular, no need for
anything finer), not event-driven off ingestion. Two rule types (see
`ClientApp/src/models/alert.vm.ts`, the source of truth this mirrors):
"threshold" (`Metric` -- `error_rate` | `log_volume` -- over `WindowMinutes`,
optionally scoped to `ServiceName`, breaches when it exceeds
`ThresholdValue`) and "absence" (breaches when `ServiceName`, or every
service combined if null, produced zero logs in `WindowMinutes`). Both reuse
`GetErrorRateKpiAsync`/`GetLogVolumeKpiAsync` -- the exact same queries the
dashboard's own KPI cards run -- rather than parallel query logic.

State transitions only: `INafasQueryStore.GetOpenIncidentForRuleAsync` is
checked before creating a new incident, so a rule that stays breached across
evaluations does NOT re-fire (and re-POST the webhook) every 30s -- only on
open and on resolve. Fires both `NafasLiveFeed`'s `"alerts"` SSE channel and
`INafasAlertWebhookSender` on each transition.

**Frontend gap, not a backend bug**: `alerts.vue`'s incident list
(`رخدادها`) doesn't live-refresh off the `"alerts"` SSE push -- a real
incident opens/resolves correctly in the database and appears correctly on
the next page load/navigation, it just doesn't appear without one. Fixing
that is a frontend task, not part of evaluation itself.

## Known temporary/incomplete pieces (check before assuming these are final)

- The SaaS platform's multi-channel "Notification channels" UI section and
  its tenant-level push toggle were both **removed outright**, not stubbed,
  from `alerts.vue` -- neither matches this package's actual delivery
  model: one developer-configured webhook
  (`NafasServerOptions.AlertWebhookUrl`, `NafasAlertWebhookSender.cs`), set
  in code, not managed at runtime, and no tenant concept to toggle a
  preference for. If a real per-installation channel system is ever
  wanted, it's a new feature to design, not a gap to fill.
- Licensed under **FSL-1.1-ALv2** (Functional Source License, source-available
  -- free for any use except building a competing product/service off it;
  converts to Apache-2.0 two years after each version's release). See
  `Nafas.Observability/LICENSE.md` (the authoritative English text,
  byte-faithful to the canonical FSL template) and
  `Nafas.Observability/LICENSE.fa.md` (a fluent Persian translation for
  convenience only -- explicitly non-binding, `LICENSE.md` governs on any
  conflict).
- NuGet packaging metadata is set on `Nafas.Observability.csproj`
  (`PackageId`/`Version`/`Authors`/`Company`/`Copyright`/`Description`/
  `PackageTags`/`PackageProjectUrl`/`RepositoryUrl`/`PackageLicenseFile`/
  `PackageReadmeFile`/`PackageIcon`), verified with a real `dotnet pack`.
  `Version` is `0.1.0`, deliberately not `1.0.0` -- real ingestion/alerting
  only just landed and nothing has run this in a real deployment yet. Bump
  it by hand for each release; there's no SourceLink/CI-driven versioning
  set up. `icon.png` (512x512) was regenerated from `AppSidebar.vue`'s own
  former logo mark (teal gradient rounded square, same wave path/
  proportions) via a one-off Pillow script, since no rasterized brand asset
  existed anywhere -- kept as the package's visual identity even after that
  component's sidebar UI was simplified to typography-only.
- `Nafas.Dashboard.TestHost` is a plain `dotnet new mvc` scaffold with
  `AddNafasServer()`/`UseNafasDashboard("/nafas")` added -- it exists purely
  to manually verify the package end-to-end (see its `Program.cs`), it is
  **not** a usage example to copy patterns from otherwise. Its own
  `nafas.db` (SQLite, git-ignored) is throwaway/regenerated on every run --
  never commit it.
- No CI/CD (GitHub Actions) set up yet for build/test/pack/publish.

## Recurring gotcha

`.csproj` XML comments **cannot contain `--`** (MSB4025) -- hit repeatedly
across every `.csproj` here. Use a comma or reword instead of an
em-dash-style `--` inside any `<!-- -->` block in a project file.
