using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Nafas.Observability.Server.Internal
{
    /// <summary>
    /// Handles exactly one accepted <see cref="TcpClient"/> connection: reads
    /// and parses one HTTP/1.1 request off it, builds a real
    /// <see cref="DefaultHttpContext"/> from that (nothing here is supplied
    /// by a hosting layer -- there isn't one), runs it through the
    /// <see cref="RequestDelegate"/> built from <c>UseNafasDashboard</c>, and
    /// writes back exactly one response before closing the connection.
    ///
    /// Deliberately one request per connection, not HTTP/1.1 keep-alive --
    /// this dashboard is a local/LAN admin tool, not a public high-throughput
    /// API, and giving up connection reuse removes an entire dimension of
    /// correctness surface (request pipelining, resetting read timeouts
    /// between requests, telling apart "client is done with this connection"
    /// from "client is mid-request") for a cost (one extra TCP handshake per
    /// dashboard API call) that's imperceptible on localhost/LAN. Every
    /// response includes an explicit <c>Connection: close</c> (see
    /// NafasResponseBodyStream.cs) so well-behaved clients don't even try to
    /// reuse it.
    /// </summary>
    internal static class NafasRawHttpConnection
    {
        // 8KB matches common real server defaults for the request
        // line + headers combined (e.g. nginx's large_client_header_buffers,
        // Kestrel's own default MaxRequestLineSize) -- generous for anything
        // this dashboard's own SPA client actually sends, while still
        // bounding memory for a malformed or hostile request.
        private const int MaxLineLength = 8 * 1024;
        private const int MaxHeaderCount = 100;
        // Alert-rule create/update bodies are a few hundred bytes of JSON;
        // 10MB is generous headroom, not an attempt at a "real" limit.
        private const long MaxBodyBytes = 10 * 1024 * 1024;

        public static async Task HandleAsync(TcpClient client, RequestDelegate pipeline, IServiceProvider nafasServices, ILogger logger, CancellationToken serverShutdownToken)
        {
            using var _ = client;
            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(serverShutdownToken);

            // Small JSON envelopes and SSE frames, not bulk transfer --
            // don't hold them back for Nagle coalescing.
            client.NoDelay = true;
            var stream = client.GetStream();
            var reader = new BufferedSocketReader(stream);

            HttpContext? context = null;
            NafasResponseBodyStream? responseBody = null;
            try
            {
                var requestLine = await reader.ReadLineAsync(MaxLineLength, connectionCts.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(requestLine))
                {
                    // Client opened and closed the connection without
                    // sending anything (e.g. a port probe) -- nothing to
                    // respond to.
                    return;
                }

                var (method, path, queryString) = ParseRequestLine(requestLine!);

                context = new DefaultHttpContext
                {
                    RequestAborted = connectionCts.Token,
                    RequestServices = nafasServices,
                };
                context.Request.Method = method;
                context.Request.Scheme = "http";
                context.Request.PathBase = PathString.Empty;
                context.Request.Path = new PathString(path);
                context.Request.QueryString = new QueryString(queryString);
                context.Request.Protocol = "HTTP/1.1";

                await ReadHeadersAsync(reader, context.Request.Headers, connectionCts.Token).ConfigureAwait(false);

                var hostHeader = context.Request.Headers.TryGetValue("Host", out var hostValues) ? hostValues.ToString() : null;
                context.Request.Host = new HostString(string.IsNullOrEmpty(hostHeader) ? "localhost" : hostHeader);

                if (context.Request.Headers.ContainsKey("Transfer-Encoding"))
                {
                    // Never sent by this dashboard's own SPA client (fetch()
                    // always sends a plain Content-Length for a JSON body) --
                    // rejected rather than silently mishandled as an empty
                    // body.
                    throw new InvalidOperationException("Chunked request bodies are not supported.");
                }

                if (long.TryParse(context.Request.Headers["Content-Length"], out var contentLength) && contentLength > 0)
                {
                    if (contentLength > MaxBodyBytes)
                    {
                        throw new InvalidOperationException($"Request body exceeds the {MaxBodyBytes}-byte limit.");
                    }
                    var body = await reader.ReadExactAsync((int)contentLength, connectionCts.Token).ConfigureAwait(false);
                    context.Request.Body = new MemoryStream(body, writable: false);
                    context.Request.ContentLength = contentLength;
                }
                else
                {
                    context.Request.Body = Stream.Null;
                }

                SetConnectionInfo(context, client);

                responseBody = new NafasResponseBodyStream(stream, context.Response);
                context.Response.Body = responseBody;

                await pipeline(context).ConfigureAwait(false);
                await responseBody.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && serverShutdownToken.IsCancellationRequested))
            {
                logger.LogError(ex, "Nafas raw HTTP connection failed while handling a request.");
                await TryWriteFallbackErrorAsync(context, responseBody, ex).ConfigureAwait(false);
            }
        }

        private static async Task ReadHeadersAsync(BufferedSocketReader reader, IHeaderDictionary headers, CancellationToken ct)
        {
            var count = 0;
            while (true)
            {
                var line = await reader.ReadLineAsync(MaxLineLength, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(line)) return; // blank line -- end of headers

                if (++count > MaxHeaderCount)
                {
                    throw new InvalidOperationException($"Request has more than {MaxHeaderCount} headers.");
                }

                var colonIndex = line!.IndexOf(':');
                if (colonIndex < 0)
                {
                    throw new InvalidOperationException("Malformed header line (no ':').");
                }

                var name = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();
                headers[name] = value;
            }
        }

        private static (string Method, string Path, string QueryString) ParseRequestLine(string line)
        {
            var parts = line.Split(new[] { ' ' }, 3);
            if (parts.Length != 3)
            {
                throw new InvalidOperationException("Malformed request line.");
            }

            var method = parts[0];
            var target = parts[1];
            // parts[2] is the HTTP version the client sent -- intentionally
            // ignored; every response here is always framed as HTTP/1.1
            // regardless (see NafasResponseBodyStream), and the dashboard's
            // own SPA client never sends anything else anyway.

            var queryIndex = target.IndexOf('?');
            var rawPath = queryIndex < 0 ? target : target.Substring(0, queryIndex);
            var query = queryIndex < 0 ? string.Empty : target.Substring(queryIndex);

            // HttpRequest.Path is the DECODED path (matches how Kestrel
            // itself populates it); HttpRequest.QueryString stays raw --
            // HttpRequest.Query does its own decoding lazily on first access.
            return (method, Uri.UnescapeDataString(rawPath), query);
        }

        // Load-bearing for NafasServerOptions.Authorize's default
        // "local requests only" check (NafasDashboardExtensions.IsLocalRequest),
        // which reads Connection.RemoteIpAddress/LocalIpAddress directly --
        // get this wrong and every request looks "local" regardless of
        // NafasHttpServerAccessMode, silently defeating the whole point of
        // that default.
        private static void SetConnectionInfo(HttpContext context, TcpClient client)
        {
            if (client.Client.RemoteEndPoint is IPEndPoint remote)
            {
                context.Connection.RemoteIpAddress = remote.Address;
                context.Connection.RemotePort = remote.Port;
            }
            if (client.Client.LocalEndPoint is IPEndPoint local)
            {
                context.Connection.LocalIpAddress = local.Address;
                context.Connection.LocalPort = local.Port;
            }
        }

        private static async Task TryWriteFallbackErrorAsync(HttpContext? context, NafasResponseBodyStream? responseBody, Exception ex)
        {
            if (context is null || responseBody is null || responseBody.HeadersSent)
            {
                // Either parsing failed before there was anywhere to send a
                // response, or a streaming response already sent real bytes
                // (the SSE endpoints, most likely) -- in the latter case a
                // client just sees the connection end abruptly, the same as
                // any server that breaks mid-stream; there's no clean way to
                // retract a status line already on the wire.
                return;
            }

            try
            {
                context.Response.Headers.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "text/plain; charset=utf-8";
                var bytes = Latin1.GetBytes("Nafas dashboard error: " + ex.Message);
                context.Response.ContentLength = bytes.Length;
                await responseBody.WriteAsync(bytes, 0, bytes.Length, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The connection itself may already be broken -- nothing
                // more this handler can do.
            }
        }
    }
}
