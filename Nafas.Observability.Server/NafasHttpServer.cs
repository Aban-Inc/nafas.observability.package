using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Server.Internal;

namespace Nafas.Observability.Server
{
    /// <summary>
    /// Runs a standalone HTTP/1.1 listener that serves the Nafas dashboard
    /// on its own port -- for apps (WinForms, WPF, a plain console/worker
    /// service) with no ASP.NET Core pipeline of their own to reach it any
    /// other way. Built directly on <see cref="TcpListener"/> -- no Kestrel,
    /// no OWIN; see NafasRawHttpConnection.cs's own comment for the full
    /// reasoning and Nafas.Observability.Server.csproj's own comment for why
    /// <see cref="System.Net.HttpListener"/> (the BCL's own listener) wasn't
    /// usable here either.
    ///
    /// Registered as a singleton by <c>AddNafasHttpServer</c> both as itself
    /// (for callers that want to start/stop/restart it on demand -- e.g. a
    /// settings screen's "enable dashboard server" checkbox) and as an
    /// <see cref="IHostedService"/> (so <see cref="NafasHttpServerOptions.Enabled"/>
    /// being true at process startup starts it automatically).
    ///
    /// Deliberately does NOT build its own copy of Nafas's services (no
    /// second <c>AddNafasServer()</c>, no second ingestion/retention/alert
    /// hosted services) -- it reuses the host app's own already-built
    /// <see cref="IServiceProvider"/> via the
    /// <c>UseNafasDashboard(IApplicationBuilder, IServiceProvider, string)</c>
    /// overload, so this listener shows the exact same running app's real
    /// data, not a second, disconnected copy of it.
    /// </summary>
    public sealed class NafasHttpServer : IHostedService, IAsyncDisposable
    {
        private readonly IServiceProvider _nafasServices;
        private readonly NafasHttpServerOptions _configuredOptions;
        private readonly ILogger<NafasHttpServer> _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private TcpListener? _listener;
        private CancellationTokenSource? _acceptLoopCts;
        private Task? _acceptLoopTask;

        public NafasHttpServer(IServiceProvider nafasServices, NafasHttpServerOptions configuredOptions, ILogger<NafasHttpServer> logger)
        {
            _nafasServices = nafasServices ?? throw new ArgumentNullException(nameof(nafasServices));
            _configuredOptions = configuredOptions ?? throw new ArgumentNullException(nameof(configuredOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Whether the listener is currently running.</summary>
        public bool IsRunning => _listener is not null;

        /// <summary>The port it's currently listening on, or null if not running.</summary>
        public int? Port { get; private set; }

        // IHostedService.StartAsync -- runs once at process startup. Only
        // actually starts the listener if NafasHttpServerOptions.Enabled was
        // true at AddNafasHttpServer() call time (see that property's own
        // comment on why this is a one-time check, not a live setting).
        Task IHostedService.StartAsync(CancellationToken cancellationToken) =>
            _configuredOptions.Enabled
                ? StartAsync(_configuredOptions.Port, _configuredOptions.AccessMode, _configuredOptions.DashboardPath, cancellationToken)
                : Task.CompletedTask;

        Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

        /// <summary>
        /// Starts (or restarts, if already running -- e.g. the port or
        /// access mode just changed) the listener. Safe to call from a
        /// settings screen whenever the user changes something, not just at
        /// startup.
        /// </summary>
        public async Task StartAsync(int port, NafasHttpServerAccessMode accessMode, string dashboardPath, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);

                // Same "fail loudly on a missing prerequisite call" posture
                // as UseNafasDashboard's own check -- a missing
                // AddNafasServer() is a setup bug that should surface
                // immediately, not as a dashboard that starts and shows
                // nothing.
                _ = _nafasServices.GetService<NafasServerOptions>()
                    ?? throw new InvalidOperationException("AddNafasHttpServer requires AddNafasServer() to be called first, e.g. services.AddNafasServer().");

                var address = accessMode == NafasHttpServerAccessMode.Lan ? IPAddress.Any : IPAddress.Loopback;
                var listener = new TcpListener(address, port);
                try
                {
                    listener.Start();
                }
                catch (SocketException ex)
                {
                    // The single most likely real-world failure (another
                    // process already bound to the port) -- wrapped with the
                    // actual port number, since the raw SocketException
                    // message doesn't always make that obvious at a glance.
                    throw new IOException($"Nafas HTTP server failed to start on port {port} -- it's likely already in use by another process. Set a different NafasHttpServerOptions.Port.", ex);
                }

                // ApplicationServices here is a fallback-wrapped provider
                // (see NafasFallbackServiceProvider.cs), not _nafasServices
                // directly -- StaticFileMiddleware (real ASP.NET Core
                // middleware, activated via ActivatorUtilities) needs an
                // IHostingEnvironment/ILoggerFactory to construct at all,
                // which a plain WinForms/WPF/console app's own provider was
                // never going to have registered. UseNafasDashboard's own
                // dependencies (NafasServerOptions, INafasQueryStore, etc.)
                // still come from the real _nafasServices passed explicitly
                // below, not from this wrapper.
                var appBuilder = new ApplicationBuilder(new NafasFallbackServiceProvider(_nafasServices));
                appBuilder.UseNafasDashboard(_nafasServices, dashboardPath);
                var pipeline = appBuilder.Build();

                var acceptCts = new CancellationTokenSource();
                var acceptTask = AcceptLoopAsync(listener, pipeline, acceptCts.Token);

                _listener = listener;
                _acceptLoopCts = acceptCts;
                _acceptLoopTask = acceptTask;
                Port = port;

                _logger.LogInformation(
                    "Nafas HTTP server listening ({AccessMode}) on port {Port} at {Path}",
                    accessMode, port, dashboardPath);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Stops the listener. A no-op if it isn't running.</summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await StopCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        // Caller must hold _gate.
        private async Task StopCoreAsync()
        {
            var listener = _listener;
            var acceptLoopCts = _acceptLoopCts;
            var acceptLoopTask = _acceptLoopTask;
            _listener = null;
            _acceptLoopCts = null;
            _acceptLoopTask = null;
            Port = null;

            if (listener is null) return;

            acceptLoopCts!.Cancel();
            // Unblocks a pending AcceptTcpClientAsync() -- netstandard2.0's
            // TcpListener has no cancellable accept overload, so closing the
            // listener out from under it is the standard way to abort one.
            listener.Stop();

            try
            {
                await acceptLoopTask!.ConfigureAwait(false);
            }
            catch
            {
                // Expected: the accept loop observes the Stop() above as an
                // exception on its in-flight AcceptTcpClientAsync() call.
            }

            acceptLoopCts.Dispose();
        }

        private async Task AcceptLoopAsync(TcpListener listener, RequestDelegate pipeline, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch when (ct.IsCancellationRequested)
                {
                    return; // expected: StopCoreAsync() called listener.Stop() deliberately
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nafas HTTP server failed to accept a connection.");
                    continue;
                }

                _ = HandleConnectionSafeAsync(client, pipeline, ct);
            }
        }

        private async Task HandleConnectionSafeAsync(TcpClient client, RequestDelegate pipeline, CancellationToken ct)
        {
            try
            {
                await NafasRawHttpConnection.HandleAsync(client, pipeline, _nafasServices, _logger, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled failure in a Nafas HTTP server connection.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _gate.Dispose();
        }
    }
}
