namespace Nafas.Observability.Server
{
    /// <summary>
    /// Configuration for <c>services.AddNafasHttpServer(options => { ... })</c>.
    /// Governs the standalone HTTP listener <see cref="NafasHttpServer"/>
    /// starts, not the dashboard's own data/behavior -- that's still
    /// <see cref="NafasServerOptions"/>, configured separately via
    /// <c>AddNafasServer()</c> (required first; see that call's own docs).
    /// </summary>
    public class NafasHttpServerOptions
    {
        /// <summary>
        /// Whether the listener should actually start. Defaults to
        /// <c>false</c> -- this package does nothing at all until an app
        /// explicitly opts in, e.g. from a persisted user setting:
        /// <code>
        /// services.AddNafasHttpServer(o =&gt;
        /// {
        ///     o.Enabled = settings.DashboardServerEnabled;
        ///     o.Port = settings.DashboardServerPort;
        /// });
        /// </code>
        /// Only read once, at process startup (<c>IHostedService.StartAsync</c>).
        /// To start/stop/reconfigure the listener while the app is already
        /// running -- e.g. the user just flipped the checkbox in a settings
        /// screen -- call <see cref="NafasHttpServer"/>'s own
        /// <c>StartAsync</c>/<c>StopAsync</c> directly instead (resolve it
        /// from DI).
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The TCP port to listen on. Defaults to 5099 -- outside the
        /// common well-known/registered-port ranges apps are more likely to
        /// already be using, but there's nothing special about it; pick
        /// whatever's free on the target machine (and let the user
        /// reconfigure it -- a hardcoded port is exactly the kind of thing
        /// that collides on someone else's machine).
        /// </summary>
        public int Port { get; set; } = 5099;

        /// <summary>
        /// Which network interface(s) to bind -- see
        /// <see cref="NafasHttpServerAccessMode"/>'s own comment, including
        /// why this alone doesn't open the dashboard up to the network
        /// without also setting <see cref="NafasServerOptions.Authorize"/>.
        /// Defaults to <see cref="NafasHttpServerAccessMode.LocalhostOnly"/>.
        /// </summary>
        public NafasHttpServerAccessMode AccessMode { get; set; } = NafasHttpServerAccessMode.LocalhostOnly;

        /// <summary>
        /// The path the dashboard is mounted at on this listener, passed
        /// straight through to <c>UseNafasDashboard</c>. Defaults to
        /// "/nafas", same as the embedded-dashboard path's own default.
        /// </summary>
        public string DashboardPath { get; set; } = "/nafas";
    }
}
