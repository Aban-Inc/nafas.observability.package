using Nafas.Observability;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// ---- Nafas.Observability -----------------------------------------------
//
// Every NafasServerOptions option is set (or shown, commented out) below --
// unlike a truly zero-config setup (just `AddNafasServer()` with nothing
// else, which also works and is exactly what this same line looked like
// before), this is meant to double as a working reference for what's
// actually configurable. See NafasServerOptions.cs's own XML doc comments
// for the full reasoning behind each default -- this only summarizes.
builder.Services.AddNafasServer(options =>
{
    // -- Storage: which database, and how to reach it --

    // DatabaseProvider.Sqlite (the default) needs nothing else installed --
    // a single file next to the app, created automatically on first run.
    // Set DatabaseProvider.SqlServer instead for a shared/production
    // database; see ConnectionString/ConnectionStringName/Schema below,
    // all of which only apply to SqlServer.
    options.Provider = DatabaseProvider.Sqlite;

    // The connection string itself. For SQLite, left unset here on purpose
    // to exercise its own default (null -> "Data Source=nafas.db" in this
    // app's content root) -- uncomment to point it somewhere else instead:
    // options.ConnectionString = "Data Source=nafas-testhost.db";
    //
    // For SqlServer, either this or ConnectionStringName below is required
    // -- there's no sensible SqlServer default to fall back to. Example:
    // options.Provider = DatabaseProvider.SqlServer;
    // options.ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=NafasTestHost;Trusted_Connection=True;";

    // Alternative to setting ConnectionString directly: the NAME of a
    // connection string this app already defines in appsettings.json's
    // "ConnectionStrings" section (or classic web.config's
    // <connectionStrings>, for .NET Framework apps). Takes lower priority
    // than ConnectionString when both are set -- see
    // ConnectionStringResolver.cs for the exact resolution order.
    // options.ConnectionStringName = "NafasDb";

    // SqlServer only -- keeps Nafas's tables under a named schema instead
    // of the connection's default ("dbo"), so they stay clearly separated
    // if this app shares a database with its own tables. Ignored for
    // SQLite, which has no schema concept.
    // options.Schema = "nafas";

    // -- Retention: how long ingested data is kept --

    // How many days of logs/metrics/traces the background retention sweep
    // keeps before deleting them. 30 is the default; set explicitly here
    // just to show it's a plain int, not something more exotic.
    options.RetentionDays = 30;

    // -- Alerting: where incident open/resolve events get delivered --

    // Alert rules and incidents work and show up in the dashboard's Alerts
    // page regardless of this setting -- nothing gets delivered anywhere
    // until it's set. Point this at https://webhook.site (or your own
    // endpoint) to see real JSON payloads land somewhere while trying this
    // out locally; see NafasAlertWebhookSender.cs for the exact payload
    // shape and retry behavior.
    // options.AlertWebhookUrl = "https://example.com/webhooks/nafas";

    // -- Identity: what this app calls itself in its own data --

    // The value stamped on every ingested log/metric/trace row's
    // ServiceName column. Defaults to IHostEnvironment.ApplicationName
    // (this project's own assembly name, "Nafas.Dashboard.TestHost" --
    // set explicitly here anyway so this line stays correct even if the
    // project is ever renamed or copied elsewhere). Set this yourself when
    // running multiple differently-named instances of the same app that
    // share one database.
    options.ServiceName = "Nafas.Dashboard.TestHost";

    // -- Security: who's allowed to load the dashboard --

    // Gates every request to the dashboard mounted below by
    // app.UseNafasDashboard(path). Left unset (null) here deliberately --
    // that's the safe default every real consumer starts from too: "local
    // requests only", the same posture as Hangfire's own dashboard (see
    // NafasDashboardExtensions.IsLocalRequest). Uncomment to open it up,
    // e.g. behind your own authentication/authorization:
    // options.Authorize = httpContext => httpContext.User.IsInRole("Admin");
    //
    // Or, deliberately, to everyone (not recommended without your own auth
    // in front of it, and never on a host reachable beyond your own
    // machine):
    // options.Authorize = _ => true;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

// Mounted at a path this consuming app picked itself -- "/nafas" here is
// just this test host's own choice, not a hardcoded default the package
// forces on anyone (see NafasDashboardExtensions.UseNafasDashboard's own
// `path` parameter). Try a different string here to confirm that.
app.UseNafasDashboard("/nafas");

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
