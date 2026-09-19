# Nafas.Observability

[![NuGet](https://img.shields.io/nuget/v/Nafas.Observability?logo=nuget&label=Nafas.Observability)](https://www.nuget.org/packages/Nafas.Observability)
[![NuGet](https://img.shields.io/nuget/v/Nafas.Observability.Server?logo=nuget&label=Nafas.Observability.Server)](https://www.nuget.org/packages/Nafas.Observability.Server)
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
- [Desktop and other non-ASP.NET Core apps](#desktop-and-other-non-aspnet-core-apps)
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

## Desktop and other non-ASP.NET Core apps

`UseNafasDashboard` needs an `IApplicationBuilder` to mount onto — a plain
WinForms, WPF, or console/worker app has no ASP.NET Core pipeline of its
own to provide one. `Nafas.Observability.Server` is a separate, optional
package for exactly that case: it runs a second, independent HTTP listener
(on its own port) that serves the same dashboard against the same running
app's real data — no Kestrel, no OWIN, just a minimal HTTP/1.1 server
directly on `TcpListener` (see
[`Nafas.Observability.Server/Nafas.Observability.Server.csproj`](Nafas.Observability.Server/Nafas.Observability.Server.csproj)'s
own comment for why: the last version of Kestrel ever published as a plain
NuGet package is 2.3.13 from 2019, with no HTTP/2 and no security patches
since, and `System.Net.HttpListener` — the BCL's own listener — turned out
not to be usable for the LAN case either: on Windows it's backed by
HTTP.sys, which refuses to bind any prefix other than "localhost" unless
the process runs elevated or a URL ACL was reserved beforehand, verified
empirically). Being plain `netstandard2.0` like the core package itself,
this also works from classic .NET Framework 4.6.1+ apps, not just modern
.NET — confirmed against a real net48 console host, not just the TFM
compatibility rules, in
[`Nafas.Observability.Server.NetFrameworkSample`](Nafas.Observability.Server.NetFrameworkSample)
(see [Requirements](#requirements) for the one net48-specific gotcha it
also caught, around SQLite and `PlatformTarget`).

```bash
dotnet add package Nafas.Observability.Server
```

```csharp
services.AddNafasServer();               // same as always -- storage + ingestion
services.AddNafasHttpServer(o =>
{
    o.Enabled = true;                    // opt-in; does nothing while false
    o.Port = 5099;
    o.AccessMode = NafasHttpServerAccessMode.LocalhostOnly; // or .Lan
});
```

then open `http://localhost:5099/nafas`. `NafasHttpServerOptions.Enabled` is
only read once at startup — to start/stop/reconfigure it later (e.g. a
settings screen's "enable dashboard" checkbox), resolve `NafasHttpServer`
from DI and call its `StartAsync`/`StopAsync` directly; see
[`Nafas.Observability.Server.Sample/`](Nafas.Observability.Server.Sample)
for a worked WPF example.

`AccessMode` only controls which network interface is bound —
`LocalhostOnly` (the default) binds loopback only, `Lan` binds every
interface so other devices on the network can reach the port via this
machine's own IP, with no administrator rights required either way (unlike
`System.Net.HttpListener`, verified empirically on both). It does not by
itself relax `NafasServerOptions.Authorize` (still local-requests-only by
default, see [Security](#security)): set `Authorize` explicitly too if
`Lan` should actually let those requests through, otherwise they still get
a `403`.

One request per connection, not HTTP/1.1 keep-alive — this listener is a
local/LAN admin tool, not a public high-throughput API, so giving up
connection reuse (an extra TCP handshake per dashboard API call,
imperceptible on localhost/LAN) removes an entire dimension of correctness
surface instead. See
[`Nafas.Observability.Server/Internal/NafasRawHttpConnection.cs`](Nafas.Observability.Server/Internal/NafasRawHttpConnection.cs)'s
own comment.

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
- ASP.NET Core only for the core package — `UseNafasDashboard` requires
  `IApplicationBuilder`. For WinForms/WPF/console apps, see
  [`Nafas.Observability.Server`](#desktop-and-other-non-aspnet-core-apps)
  instead.
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
- **Classic .NET Framework + SQLite: set an explicit `PlatformTarget`.**
  `netstandard2.0` compatibility means .NET Framework can *reference* this
  package, but the SQLite native driver still needs to know which
  architecture to load. On .NET Core/.NET 5+, the runtime's own RID-graph
  resolves this automatically regardless of how the project was built; on
  classic .NET Framework there's no such mechanism, so an `AnyCPU` build
  can copy a native `e_sqlite3.dll` that doesn't match the process's actual
  bitness at run time, failing with `Library e_sqlite3 not found` / `%1 is
  not a valid Win32 application`. Set `<PlatformTarget>x64</PlatformTarget>`
  (or `x86`, matching your deployment) and `<Prefer32Bit>false</Prefer32Bit>`
  explicitly in the consuming `.csproj` to fix it — verified against a real
  net48 console host in
  [`Nafas.Observability.Server.NetFrameworkSample`](Nafas.Observability.Server.NetFrameworkSample),
  see that project's own `.csproj` comment. SQL Server storage isn't
  affected (no native driver to resolve this way).

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
- [`Nafas.Observability.Server/`](Nafas.Observability.Server) — the
  optional standalone HTTP listener package for non-ASP.NET Core apps (no
  Kestrel, no OWIN); see [Desktop and other non-ASP.NET Core apps](#desktop-and-other-non-aspnet-core-apps).
- [`Nafas.Observability.Server.Sample/`](Nafas.Observability.Server.Sample) —
  a WPF app demonstrating it, with a settings-screen-style checkbox to
  enable/disable the dashboard server, pick its port, and choose
  localhost-only vs. LAN access at runtime.
- [`Nafas.Observability.Server.NetFrameworkSample/`](Nafas.Observability.Server.NetFrameworkSample) —
  a plain `net48` console app; not a usage pattern to copy (a real .NET
  Framework consumer wouldn't normally need the Generic Host boilerplate
  shown here), but an actual classic .NET Framework process used to
  empirically verify the whole stack (storage, ingestion, and the
  `TcpListener`-based HTTP server) rather than just relying on
  `netstandard2.0`'s TFM compatibility claim.

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

**Feature-complete for the free tier.** Logs, metrics, traces, alerting,
the embedded dashboard, and the standalone desktop/console server
(`Nafas.Observability.Server`) are all shipped — no further feature work
is planned for this scope. From here, changes to the free tier are bug
fixes and hardening, not new capability.

Still pre-1.0 (see the NuGet badges at the top of this file for the exact
versions), and that status is about proof, not scope: real ingestion,
storage, dashboard, and alert evaluation all work and are exercised
through `Nafas.Dashboard.TestHost`, but neither package has yet run in a
real production deployment or been through a real upgrade cycle. The
public API surface (`NafasServerOptions`, `UseNafasDashboard`) is expected
to stay stable regardless.

## License

[FSL-1.1-ALv2](https://fsl.software) — free to use, modify and redistribute
for any purpose other than building a competing product or service; each
release converts to the Apache License, Version 2.0 two years after it
ships. See [LICENSE.md](Nafas.Observability/LICENSE.md).
