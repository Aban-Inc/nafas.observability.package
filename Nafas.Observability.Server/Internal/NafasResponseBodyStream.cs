using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Nafas.Observability.Server.Internal
{
    /// <summary>
    /// <see cref="HttpContext.Response"/>'s <c>Body</c> stream for a raw
    /// connection -- writes the status line and headers lazily, on the
    /// first byte written (or on <see cref="CompleteAsync"/> if the handler
    /// never wrote anything at all), the same way a real server defers
    /// headers until it either knows the full response or must start
    /// streaming.
    ///
    /// Framing is decided at that moment: if the handler already set
    /// <c>Response.ContentLength</c> (e.g. <c>StaticFileMiddleware</c>,
    /// which knows a file's length upfront), the body is written as exactly
    /// that many raw bytes. Otherwise (every JSON envelope and both SSE
    /// streams in NafasDashboardEndpoints.cs -- none of them know their
    /// total length upfront) it falls back to HTTP/1.1 chunked
    /// transfer-encoding, terminated in <see cref="CompleteAsync"/>. This
    /// mirrors how Kestrel itself picks between the two -- nothing above
    /// this stream (StaticFileMiddleware, NafasDashboardEndpoints.cs) needs
    /// to know or care which framing it ends up with.
    /// </summary>
    internal sealed class NafasResponseBodyStream : Stream
    {
        private enum Framing { ContentLength, Chunked }

        private static readonly byte[] Crlf = Latin1.GetBytes("\r\n");

        private readonly Stream _connection;
        private readonly HttpResponse _response;
        private bool _headersSent;
        private Framing _framing = Framing.ContentLength;

        public NafasResponseBodyStream(Stream connection, HttpResponse response)
        {
            _connection = connection;
            _response = response;
        }

        /// <summary>
        /// Whether the status line and headers have already gone out on the
        /// wire -- once true, a caller can no longer change
        /// <c>Response.StatusCode</c>/<c>Headers</c> and have it take
        /// effect, and any error from this point on can only end the
        /// connection, not send a clean error response instead.
        /// </summary>
        public bool HeadersSent => _headersSent;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await EnsureHeadersSentAsync(cancellationToken).ConfigureAwait(false);
            if (count == 0) return;

            if (_framing == Framing.Chunked)
            {
                var sizeLine = Latin1.GetBytes(count.ToString("x") + "\r\n");
                await _connection.WriteAsync(sizeLine, 0, sizeLine.Length, cancellationToken).ConfigureAwait(false);
                await _connection.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                await _connection.WriteAsync(Crlf, 0, Crlf.Length, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _connection.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            }
        }

        public override void Flush() => FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await EnsureHeadersSentAsync(cancellationToken).ConfigureAwait(false);
            await _connection.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureHeadersSentAsync(CancellationToken ct)
        {
            if (_headersSent) return;
            _headersSent = true;

            _framing = _response.ContentLength.HasValue ? Framing.ContentLength : Framing.Chunked;

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(_response.StatusCode).Append(' ').Append(ReasonPhrases.Get(_response.StatusCode)).Append("\r\n");
            foreach (var header in _response.Headers)
            {
                foreach (var value in header.Value)
                {
                    if (value is null) continue;
                    sb.Append(header.Key).Append(": ").Append(value).Append("\r\n");
                }
            }
            if (_framing == Framing.Chunked)
            {
                sb.Append("Transfer-Encoding: chunked\r\n");
            }
            // Every connection here is one request/response, then closed --
            // see NafasRawHttpConnection.cs's own comment on why keep-alive
            // isn't supported. Telling the client this explicitly is correct
            // HTTP/1.1 (default is persistent unless said otherwise), not
            // just an implementation shortcut.
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");

            var bytes = Latin1.GetBytes(sb.ToString());
            await _connection.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Called once by the connection handler after the request pipeline
        /// finishes handling the request. Ensures headers go out even for a
        /// literal zero-byte body, and terminates chunked framing with the
        /// required <c>0\r\n\r\n</c> final chunk.
        /// </summary>
        public async Task CompleteAsync(CancellationToken ct)
        {
            await EnsureHeadersSentAsync(ct).ConfigureAwait(false);
            if (_framing == Framing.Chunked)
            {
                var terminator = Latin1.GetBytes("0\r\n\r\n");
                await _connection.WriteAsync(terminator, 0, terminator.Length, ct).ConfigureAwait(false);
            }
        }

        // The underlying connection stream/socket is owned and disposed by
        // NafasRawHttpConnection, not by this Stream -- Dispose here is
        // deliberately a no-op.
        protected override void Dispose(bool disposing)
        {
        }
    }
}
