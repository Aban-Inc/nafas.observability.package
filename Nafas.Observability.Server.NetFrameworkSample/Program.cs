using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nafas.Observability;
using Nafas.Observability.Server;

namespace Nafas.Observability.Server.NetFrameworkSample
{
    // This is the whole point of this project: an actual classic .NET
    // Framework 4.8 process (see the .csproj's own comment), not just a
    // netstandard2.0-compatibility argument on paper. If this runs and the
    // dashboard at http://localhost:5100/nafas shows real data, that
    // empirically confirms:
    //   1. Nafas.Observability + Nafas.Observability.Server load and run on
    //      net48 with no missing binding redirects.
    //   2. The raw TcpListener-based HTTP server in Nafas.Observability.Server
    //      actually accepts connections and serves the dashboard from here,
    //      not just from modern .NET.
    //   3. NafasResourceMetricsHostedService's GlobalMemoryStatusEx fallback
    //      (see its own comment) is what actually reports memory_usage here,
    //      since GC.GetGCMemoryInfo() genuinely doesn't exist on net48 -- the
    //      dashboard's Metrics page showing a non-zero value is proof this
    //      fallback path, never exercised on a real .NET Framework host
    //      before, actually works.
    internal static class Program
    {
        private static async Task Main()
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    // Storage + ingestion -- SQLite's zero-config default
                    // ("Data Source=nafas.db"), same as every other sample.
                    services.AddNafasServer(options =>
                    {
                        options.ServiceName = "Nafas.NetFrameworkSample";
                    });

                    // The standalone listener under test. LocalhostOnly is
                    // enough to prove the TcpListener path works; the Lan
                    // case was already verified empirically (via a scratch
                    // console app) on modern .NET when this package was
                    // built and doesn't depend on the host TFM at all.
                    services.AddNafasHttpServer(options =>
                    {
                        options.Enabled = true;
                        options.Port = 5100;
                        options.AccessMode = NafasHttpServerAccessMode.LocalhostOnly;
                    });
                })
                .Build();

            Console.WriteLine("Nafas.Observability.Server.NetFrameworkSample");
            Console.WriteLine("Running on: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine("Dashboard: http://localhost:5100/nafas");
            Console.WriteLine("Press Ctrl+C to stop.");

            // RunAsync (Ctrl+C / process-kill shutdown), not a manual
            // StartAsync + Console.ReadLine() + StopAsync -- ReadLine blocks
            // on stdin, which isn't always a real interactive console (a
            // Windows service, a detached/redirected process, this very
            // project's own automated verification run), so the app would
            // exit immediately on EOF instead of actually staying up.
            // RunAsync's shutdown trigger is the host's own lifetime token,
            // the standard Generic Host pattern every other sample/consumer
            // in this repo should also follow for a real long-running app.
            await host.RunAsync();
        }
    }
}
