using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nafas.Observability;
using Nafas.Observability.Server;

namespace Nafas.Observability.Server.Sample;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // The Generic Host, not WPF, owns startup/shutdown of Nafas's own
    // hosted services (ingestion, retention, alert evaluation, and --
    // whenever the checkbox in MainWindow turns it on -- the raw HTTP
    // listener too). Kept alive for the app's whole lifetime and disposed
    // in OnExit.
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Zero-config default (SQLite next to the .exe, 30-day
                // retention) -- point NafasServerOptions at SQL Server, a
                // shared connection string, etc. here for anything beyond a
                // single-user desktop app. This call is what actually turns
                // on ingestion: every ILogger<T>/Activity/Meter anywhere in
                // this app starts flowing into Nafas from here on.
                services.AddNafasServer();

                // Registered disabled (Enabled = false): this sample starts
                // the listener from MainWindow's own checkbox instead, to
                // demonstrate turning it on/off on demand (e.g. from a
                // settings screen) rather than only at process launch. A
                // real app that always wants it running would set
                // o.Enabled = true here (from a persisted user setting) and
                // never need to call NafasHttpServer.StartAsync itself.
                services.AddNafasHttpServer(o => o.Enabled = false);

                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Stops NafasHttpServer (if running) along with every other
            // hosted service, then releases the SQLite connection/etc.
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
