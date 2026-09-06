using System;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Nafas.Observability
{
    /// <summary>
    /// In-process pub/sub backing the dashboard's SSE streams
    /// (api/logs/stream, api/alerts/stream) -- the in-process equivalent of
    /// the SaaS platform repo's Redis Pub/Sub (ADR-010). No Redis here (no
    /// external dependency at all is the point of this package), and none
    /// needed: everything lives in one process, so a plain in-memory
    /// broadcast is the whole mechanism.
    ///
    /// The rule this exists to enforce: live/"recent" data on the dashboard
    /// must never be re-read from the database on a timer -- it gets pushed
    /// here, once, at write time, and every open SSE connection receives it
    /// immediately. The database is for historical queries only (search,
    /// charts over a date range, KPIs) -- see NafasDashboardEndpoints.cs's
    /// own comment on the KPI/chart caching that enforces the same rule for
    /// the request/response side.
    ///
    /// Channel names are plain strings ("logs", "alerts") rather than an
    /// enum -- keeps this reusable if a third stream shows up later without
    /// changing this file.
    /// </summary>
    public sealed class NafasLiveFeed
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<string>>> _channels = new();

        /// <summary>
        /// Publishes <paramref name="json"/> (a pre-serialized JSON payload,
        /// already in the shape the corresponding SSE consumer expects --
        /// see NafasDashboardEndpoints.cs's log/alert stream handlers) to
        /// every currently-open subscriber of <paramref name="channelName"/>.
        /// A no-op, not an error, if nobody's listening.
        /// </summary>
        public void Publish(string channelName, string json)
        {
            if (!_channels.TryGetValue(channelName, out var subscribers)) return;
            foreach (var channel in subscribers.Values)
            {
                channel.Writer.TryWrite(json);
            }
        }

        internal (Guid Id, ChannelReader<string> Reader) Subscribe(string channelName)
        {
            var subscribers = _channels.GetOrAdd(channelName, _ => new ConcurrentDictionary<Guid, Channel<string>>());
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var id = Guid.NewGuid();
            subscribers[id] = channel;
            return (id, channel.Reader);
        }

        internal void Unsubscribe(string channelName, Guid id)
        {
            if (_channels.TryGetValue(channelName, out var subscribers) && subscribers.TryRemove(id, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}
