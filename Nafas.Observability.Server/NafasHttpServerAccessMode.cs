namespace Nafas.Observability.Server
{
    /// <summary>
    /// Which network interface(s) <see cref="NafasHttpServer"/> binds its raw
    /// <see cref="System.Net.Sockets.TcpListener"/> to -- i.e. whether it's
    /// reachable only from this machine, or from other devices on the
    /// network too.
    ///
    /// This is a network-binding decision only, separate from
    /// <see cref="NafasServerOptions.Authorize"/> (the application-level
    /// gate every request still has to pass, defaulting to "local requests
    /// only" -- see that property's own comment). Setting <see cref="Lan"/>
    /// here does NOT relax that default by itself: it only makes the port
    /// reachable from other devices, it doesn't authorize their requests.
    /// Set <see cref="NafasServerOptions.Authorize"/> explicitly too (e.g.
    /// to your own auth check, or to <c>_ =&gt; true</c> if you really want
    /// it open to anyone who can reach the port) for <see cref="Lan"/> to
    /// actually let those requests through -- otherwise they'll still get a
    /// 403 from the same local-only default that protects the
    /// embedded-dashboard path.
    /// </summary>
    public enum NafasHttpServerAccessMode
    {
        /// <summary>
        /// Binds loopback only (127.0.0.1) -- reachable only from this
        /// machine, same as browsing to "localhost". The safe default:
        /// nothing outside the machine can even open a TCP connection to the
        /// port, regardless of what <see cref="NafasServerOptions.Authorize"/>
        /// is set to.
        /// </summary>
        LocalhostOnly = 0,

        /// <summary>
        /// Binds every network interface (0.0.0.0) -- reachable from other
        /// devices on the same network via this machine's own IP address
        /// (e.g. a phone on the same Wi-Fi opening
        /// <c>http://192.168.1.23:5099/nafas</c>). Combine with
        /// <see cref="NafasServerOptions.Authorize"/> -- see this enum's own
        /// comment.
        /// </summary>
        Lan = 1,
    }
}
