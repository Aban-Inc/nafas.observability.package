# Nafas.Observability

[![NuGet](https://img.shields.io/nuget/v/Nafas.Observability?logo=nuget)](https://www.nuget.org/packages/Nafas.Observability)
[![License: FSL-1.1-ALv2](https://img.shields.io/badge/license-FSL--1.1--ALv2-blue)](Nafas.Observability/LICENSE.md)
[![.NET Standard 2.0](https://img.shields.io/badge/.NET-netstandard2.0-512BD4?logo=dotnet)](Nafas.Observability/Nafas.Observability.csproj)

An embedded, self-hosted observability dashboard for ASP.NET Core: logs,
metrics, traces, and alerting, captured automatically from what your app
already produces and served from inside your own process.

```csharp
builder.Services.AddNafasServer();   // storage + real ingestion + alert evaluation
app.UseNafasDashboard("/nafas");     // mounts the dashboard UI at /nafas
```

No collector, no message broker, no external database, no Redis, no
separate process to run or deploy. One NuGet package, two lines in
`Program.cs`.

## Table of contents

- [Why Nafas](#why-nafas)
- [Quick start](#quick-start)
- [What gets captured, and how](#what-gets-captured-and-how)
- [Configuration](#configuration)
- [Security](#security)
- [Alerting](#alerting)
- [Multi-instance deployments](#multi-instance-deployments)
- [Requirements](#requirements)
- [How it's built internally](#how-its-built-internally)
- [Repository layout](#repository-layout)
- [Local development](#local-development)
- [Status](#status)
- [License](#license)

## Why Nafas

Standing up observability for a small or medium ASP.NET Core service
usually means an OTel Collector, a time-series database, and a separate
dashboard to run and secure — real infrastructure for a problem that's
often just "let me see recent errors and know when something breaks."
Nafas is the alternative for that case: it lives inside your application's
own process, stores data in a local SQLite file (or your existing SQL
Server) by default, and needs nothing external to run.

It is not a replacement for a full OTel/Prometheus/Grafana stack at scale
— it does not federate across services, it does not do long-term
high-cardinality storage, and it does not export to other backends. It is
for the case where a single self-hosted app (or a handful of them sharing
a database) needs real logs, metrics, traces, and threshold-based alerting
without introducing new infrastructure to operate.

## Quick start

```bash
dotnet add package Nafas.Observability
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNafasServer(options =>
{
    // Every option below is optional -- these are the actual defaults.
    options.Provider = DatabaseProvider.Sqlite;
    options.RetentionDays = 30;
    // options.AlertWebhookUrl = "https://example.com/webhooks/nafas";
});

var app = builder.Build();

app.UseNafasDashboard("/nafas"); // -> https://your-app/nafas

app.Run();
```

That's it — no instrumentation code to add anywhere else. Every
`ILogger` call, every `Activity`/span (including ASP.NET Core's own
per-request span), and every `Meter` measurement (including ASP.NET Core
8+'s built-in request-duration metric) your app already produces is
captured from this point on and shown on a real-time dashboard mounted
directly inside your own app, in English or Persian, light or dark.

## What gets captured, and how

Nafas hooks into the same extension points ASP.NET Core and
`Microsoft.Extensions.Logging`/`System.Diagnostics` already expose — it
does not require replacing `ILogger<T>`, wrapping `HttpClient`, or adding
any `[Trace]`-style attributes to your code:

| Signal | Source | Notes |
|---|---|---|
| Logs | An `ILoggerProvider` registered alongside your existing providers | Every `ILogger.Log*` call in the app, correlated to the active `Activity` when there is one |
| Traces | A process-wide `System.Diagnostics.ActivityListener` | Listens to every `ActivitySource`, including ASP.NET Core's own built-in per-request activity — no manual span creation needed for basic request tracing |
| Metrics | A process-wide `System.Diagnostics.Metrics.MeterListener` | Listens to every `Meter`, including ASP.NET Core 8+'s built-in `http.server.request.duration`, from which request-rate is also derived |
| Resource usage | A native sampler (`Process` + `GC.GetGCMemoryInfo()`, with a Windows-API fallback — see [Requirements](#requirements)) | CPU and memory usage of the running process, sampled on an interval |

All of it is written through a bounded, non-blocking queue
(`System.Threading.Channels`) so a burst of telemetry never applies
backpressure to your request pipeline — under sustained overload, Nafas
drops its own data rather than slow your app down. A dedicated background
service drains and batches that queue into storage, and the dashboard's
live views (the log stream, in particular) are pushed to the browser over
Server-Sent Events at write time — they are not a polling query against
the database.

## Configuration

All of these are set inside `AddNafasServer(options => { ... })`.

| Option | Default | Notes |
|---|---|---|
| `Provider` | `DatabaseProvider.Sqlite` | `Sqlite` or `SqlServer` |
| `ConnectionString` | `Data Source=nafas.db` (SQLite only) | Set directly, or use `ConnectionStringName` instead |
| `ConnectionStringName` | — | Resolves from `appsettings.json`'s `ConnectionStrings` section first, then classic `web.config`'s `<connectionStrings>` |
| `Schema` | `dbo` | SQL Server only — keeps Nafas's tables under a named schema, separate from the rest of your database |
| `RetentionDays` | `30` | How long logs/metrics/traces are kept before a background sweep deletes them |
| `AlertWebhookUrl` | — | Where alert incidents are POSTed as JSON. Alert rules and incidents still work without it; nothing gets delivered anywhere until it's set |
| `ServiceName` | `IHostEnvironment.ApplicationName` | Overrides the service name stamped on everything ingested |
| `Authorize` | `null` (local requests only) | Gates every request to the dashboard — see [Security](#security) |

`UseNafasDashboard(path)` takes the mount path for the dashboard UI itself
(default `"/nafas"`) — pick any path your app isn't already using.

## Security

The dashboard shows logs, traces, and metrics — which routinely carry
sensitive data (request bodies, stack traces, connection strings inside
exception messages). By default, `UseNafasDashboard` only serves requests
that come from the local machine — the same default posture as
[Hangfire's own dashboard](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html#configuring-authorization).
Every other request gets a `403`.

```csharp
builder.Services.AddNafasServer(options =>
{
    // Runs after your own auth middleware, if you have one registered
    // earlier in the pipeline -- httpContext.User is already populated.
    options.Authorize = httpContext => httpContext.User.IsInRole("Admin");

    // Or, deliberately, open to everyone (not recommended without your
    // own auth in front of it):
    // options.Authorize = _ => true;
});
```

If you don't set `Authorize` and the dashboard doesn't load from where you
expect (a phone on the same network, a teammate's machine, behind a
reverse proxy), this is why — set it explicitly once you know who should
be allowed in.

## Alerting

Threshold and absence rules are defined from the dashboard itself (Alerts
page) — no code required to create one. A background evaluator polls
every 30 seconds and fires **only on state transitions** (an incident
opens when a rule first goes out of bounds, and resolves when it recovers)
so a rule that stays broken for an hour doesn't re-notify every cycle.

Nafas does not send email, SMS, or Slack messages itself. When
`AlertWebhookUrl` is set, every open or resolve event is POSTed there as
JSON, and your own system takes it from there:

```json
{
  "ruleId": 12,
  "ruleName": "High error rate",
  "ruleType": "threshold",
  "status": "open",
  "detail": "error_rate is 8.4, threshold is 5",
  "triggeredAtUtc": "2026-01-15T09:32:00Z",
  "resolvedAtUtc": null
}
```

A failed webhook delivery is logged and retried on the next transition —
it never fails the request pipeline, and the incident still exists in the
dashboard's own incident list regardless of whether delivery succeeded.

## Multi-instance deployments

Nafas has no concept of a central collector — each instance that calls
`AddNafasServer()` ingests and stores its own process's telemetry. Two
common setups:

- **Single instance** — the default. Nothing to configure; `ServiceName`
  resolves to `IHostEnvironment.ApplicationName`.
- **Multiple instances sharing one SQL Server database** — point every
  instance's `ConnectionString`/`ConnectionStringName` at the same
  database and give each a distinct `ServiceName`. All of them write to
  the same tables and are distinguishable in every dashboard view by
  service name; any instance's `/nafas` mount shows data from all of
  them.

SQLite is a single-file, single-process store and is not meant to be
shared across instances — use SQL Server for that case.

## Requirements

- The package targets `netstandard2.0` for the broadest possible reach on
  the consuming app's side: **.NET Core 2.0+, .NET 5+, or classic .NET
  Framework 4.6.1+** can all reference it (`ConnectionStringName`'s
  classic `web.config` support exists for exactly that last case).
- ASP.NET Core only — `UseNafasDashboard` requires `IApplicationBuilder`.
- SQLite (zero setup) or SQL Server 2016+ for storage.
- `memoryUsage` (one of several resource metrics — logs, traces, CPU
  usage, and alerting are all unaffected either way) is read differently
  depending on the host, in this order:
  1. **.NET Core 3.0+ / .NET 5+** (the common case) — `GC.GetGCMemoryInfo()`,
     which is container-aware: it reflects a cgroup or Docker memory limit
     when one is set, not just physical host RAM.
  2. **Classic .NET Framework 4.6.1+, or .NET Core 2.x, on Windows** —
     falls back to the Win32 `GlobalMemoryStatusEx` API (the same one
     .NET Framework apps have always used for this, no managed equivalent
     exists there). This reports whole-machine physical memory, **not**
     container/Job-Object-aware, so a process capped by a Job Object
     memory limit will under-report here.
  3. **Any non-Windows host without `GC.GetGCMemoryInfo()`** (practically:
     .NET Core 2.x on Linux/macOS) — reports `0`. This path is explicitly
     gated to Windows only, so a Linux container never attempts a
     `kernel32.dll` call in the first place.

## How it's built internally

For anyone evaluating this beyond the quick start:

- **Ingestion** (`Ingestion/`) is entirely in-process: an
  `ILoggerProvider`, an `ActivityListener`, and a `MeterListener`, each
  writing into a bounded per-signal `Channel<T>` (capacity 20,000,
  drop-on-full). A single hosted service drains all three, batches
  writes (up to 500 records or every second, whichever comes first), and
  persists them.
- **Storage** (`Storage/`) is a thin interface
  (`INafasIngestionWriter`/`INafasQueryStore`) with two concrete
  implementations, SQLite and SQL Server — no ORM, parameterized SQL,
  schema created and migrated at startup.
- **Alert evaluation** (`NafasAlertEvaluationHostedService`) is a polling
  `BackgroundService` reusing the same query layer the dashboard's KPI
  views use, tracking one open-incident-per-rule at a time.
- **Real-time delivery** to the browser is an in-process pub/sub
  (`NafasLiveFeed`) fed by the ingestion writer and alert evaluator, read
  by the dashboard's SSE endpoints — nothing here is a polling query.
- **The dashboard UI** (`ClientApp/`) is a Vue 3 + Vite SPA (PrimeVue,
  Pinia, Chart.js, vue-i18n for English/Persian), built ahead of time and
  embedded directly into the assembly as embedded resources — a consumer
  installing the package gets the whole UI with no separate static-file
  deployment step, and no Node.js runtime on the host machine.
- The whole `api/*` surface (`NafasDashboardEndpoints.cs`) is a small
  hand-rolled router rather than ASP.NET Core's `IEndpointRouteBuilder`,
  because endpoint routing has been `FrameworkReference`-only since
  ASP.NET Core 3.0 and is unavailable to a `netstandard2.x` class
  library.

## Repository layout

- [`Nafas.Observability/`](Nafas.Observability) — the package itself.
- [`Nafas.Dashboard.TestHost/`](Nafas.Dashboard.TestHost) — a minimal
  ASP.NET Core app used to manually verify the package end to end; not a
  usage example to copy patterns from.

## Local development

For normal use of the published package, no Node.js/npm step is needed at
all — the dashboard UI is already built and committed under
`Nafas.Observability/wwwroot/`. Just build the solution:

```bash
dotnet build nafas.observability.sln
```

To try the package locally end to end:

```bash
cd Nafas.Dashboard.TestHost
dotnet run
```

then open `http://localhost:<port>/nafas`.

To change the dashboard UI itself:

```bash
cd Nafas.Observability/ClientApp
npm install        # first time only
npm run build       # output goes to ClientApp/dist/

# then replace (not merge) the contents of ../wwwroot/ with dist/'s contents
```

then `dotnet build` again so the updated files are re-embedded.
`.github/workflows/publish.yml` does this same rebuild-and-sync step from
source on every publish, so a tagged release never ships a stale UI.

## Status

`0.1.0` — real ingestion, storage, dashboard, and alert evaluation all
work and are exercised through `Nafas.Dashboard.TestHost`, but this has
not yet run in a production deployment. Treat it as pre-1.0: the public
API surface (`NafasServerOptions`, `UseNafasDashboard`) is expected to
stay stable, but has not been through a real upgrade cycle yet.

## License

[FSL-1.1-ALv2](https://fsl.software) — free to use, modify and redistribute
for any purpose other than building a competing product or service; each
release converts to the Apache License, Version 2.0 two years after it
ships. See [LICENSE.md](Nafas.Observability/LICENSE.md).
