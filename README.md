# Nafas.Observability

[![NuGet](https://img.shields.io/nuget/v/Nafas.Observability?logo=nuget)](https://www.nuget.org/packages/Nafas.Observability)
[![License: FSL-1.1-ALv2](https://img.shields.io/badge/license-FSL--1.1--ALv2-blue)](Nafas.Observability/LICENSE.md)
[![.NET Standard 2.1](https://img.shields.io/badge/.NET-netstandard2.1-512BD4?logo=dotnet)](Nafas.Observability/Nafas.Observability.csproj)

Self-hosted, embeddable observability dashboard for ASP.NET Core — logs,
metrics, traces, and alerts, with real ingestion and zero external
dependencies. Distributed the way [Hangfire](https://www.hangfire.io/) is:
one NuGet package, two calls, nothing else to deploy.

مستندات فارسی: [README.fa.md](README.fa.md)

```csharp
builder.Services.AddNafasServer();   // storage + real ingestion + alert evaluation
app.UseNafasDashboard("/nafas");     // mounts the dashboard UI at /nafas
```

That's it. No collector, no message broker, no external database, no Redis.
Every `ILogger` call, every `Activity`/span, and every `Meter` measurement
your app already produces is captured automatically and shown on a
real-time dashboard mounted directly inside your own app.

## Features

- **Zero-config storage** — SQLite by default (a single local file), or
  point it at SQL Server.
- **Real ingestion, zero instrumentation code** — an `ILoggerProvider`
  captures every log line; an `ActivityListener` captures every span
  (including ASP.NET Core's own per-request span); a `MeterListener`
  captures every metric (including ASP.NET Core 8+'s built-in request
  duration).
- **Actually real-time** — the dashboard's live views are pushed over
  Server-Sent Events at write time; they are never a polling query against
  the database.
- **Alerting** — define threshold or absence rules from the dashboard; a
  background evaluator opens and resolves incidents and POSTs them to a
  webhook URL you configure.
- **A full bundled UI** — Dashboard, Logs, Metrics, Traces, and Alerts
  pages; dark/light, LTR/RTL, English/Persian — embedded directly in the
  DLL, no separate static-file deployment step.
- **Nothing calls out** — no CDN fonts, no analytics, no external requests
  from the UI at all.

## Getting started

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

## Configuration

All of these are set inside `AddNafasServer(options => { ... })`.

| Option | Default | Notes |
|---|---|---|
| `Provider` | `DatabaseProvider.Sqlite` | `Sqlite` or `SqlServer` |
| `ConnectionString` | `Data Source=nafas.db` (SQLite only) | Set directly, or use `ConnectionStringName` instead |
| `ConnectionStringName` | — | Resolves from `appsettings.json`'s `ConnectionStrings` section, then classic `web.config`'s `<connectionStrings>` |
| `Schema` | `dbo` | SQL Server only -- keeps tables under a named schema |
| `RetentionDays` | `30` | How long logs/metrics/traces are kept before a background sweep deletes them |
| `AlertWebhookUrl` | — | Where alert incidents are POSTed as JSON. Alert rules and incidents still work without it; nothing gets delivered anywhere until it's set |
| `ServiceName` | `IHostEnvironment.ApplicationName` | Overrides the service name stamped on everything ingested |

## Repository layout

- [`Nafas.Observability/`](Nafas.Observability) — the package itself.
- [`Nafas.Dashboard.TestHost/`](Nafas.Dashboard.TestHost) — a minimal
  ASP.NET Core app used to manually verify the package end to end; not a
  usage example to copy patterns from.
- [`CLAUDE.md`](CLAUDE.md) — architecture notes and conventions for this
  codebase.

## License

[FSL-1.1-ALv2](https://fsl.software) — free to use, modify and redistribute
for any purpose other than building a competing product or service; each
release converts to the Apache License, Version 2.0 two years after it
ships. See [LICENSE.md](Nafas.Observability/LICENSE.md) (and
[LICENSE.fa.md](Nafas.Observability/LICENSE.fa.md) for a non-binding
Persian translation).
