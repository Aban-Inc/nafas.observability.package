using Nafas.Observability;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Default options (SQLite, 30-day retention, no webhook) -- this test host
// deliberately doesn't set anything, to confirm the zero-config default path
// actually works end to end (creates ./nafas.db next to the app on first run).
builder.Services.AddNafasServer();

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
