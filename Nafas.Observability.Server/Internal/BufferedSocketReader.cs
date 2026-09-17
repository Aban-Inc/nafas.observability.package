using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nafas.Observability.Server.Internal
{
    /// <summary>
    /// A small buffered reader over a raw connection <see cref="Stream"/>
    /// (a <see cref="System.Net.Sockets.NetworkStream"/> in practice) that
    /// supports both line-oriented reads (the request line and headers,
    /// CRLF- or bare-LF-terminated, decoded as <see cref="Latin1"/>) and
    /// exact-byte-count reads (the request body, once <c>Content-Length</c>
    /// is known) off the SAME underlying buffer -- neither <see cref="StreamReader"/>
    /// (text-only, would swallow bytes meant for the body into its own
    /// internal buffer) nor a raw unbuffered <see cref="Stream.ReadAsync(byte[],int,int)"/>
    /// loop (one syscall per few bytes while scanning for a line ending) fits
    /// both needs at once.
    /// </summary>
    internal sealed class BufferedSocketReader
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer;
        private int _pos;
        private int _len;

        public BufferedSocketReader(Stream stream, int bufferSize = 8192)
        {
            _stream = stream;
            _buffer = new byte[bufferSize];
        }

        private async Task<bool> FillAsync(CancellationToken ct)
        {
            _pos = 0;
            _len = await _stream.ReadAsync(_buffer, 0, _buffer.Length, ct).ConfigureAwait(false);
            return _len > 0;
        }

        /// <summary>
        /// Reads one line (the request line, or one header line), stripping
        /// a trailing CRLF or bare LF. Returns null only on a clean EOF with
        /// nothing at all read yet -- i.e. the client opened and closed the
        /// connection without sending anything. Throws
        /// <see cref="InvalidOperationException"/> if a single line exceeds
        /// <paramref name="maxLineLength"/> bytes, so a malformed or hostile
        /// client sending a line with no CRLF can't grow this buffer
        /// unboundedly.
        /// </summary>
        public async Task<string?> ReadLineAsync(int maxLineLength, CancellationToken ct)
        {
            using var acc = new MemoryStream();
            while (true)
            {
                if (_pos >= _len && !await FillAsync(ct).ConfigureAwait(false))
                {
                    return acc.Length == 0 ? null : DecodeLine(acc);
                }

                var newlineIndex = Array.IndexOf(_buffer, (byte)'\n', _pos, _len - _pos);
                if (newlineIndex < 0)
                {
                    acc.Write(_buffer, _pos, _len - _pos);
                    _pos = _len;
                    if (acc.Length > maxLineLength)
                    {
                        throw new InvalidOperationException($"Request line or header exceeds the {maxLineLength}-byte limit.");
                    }
                    continue;
                }

                acc.Write(_buffer, _pos, newlineIndex - _pos);
                _pos = newlineIndex + 1;
                return DecodeLine(acc);
            }
        }

        private static string DecodeLine(MemoryStream acc)
        {
            var bytes = acc.GetBuffer();
            var length = (int)acc.Length;
            // Trailing '\r' from a CRLF terminator -- a bare LF (no '\r')
            // is tolerated too, since our own SPA client always sends
            // proper CRLF but nothing is gained by rejecting the laxer form.
            if (length > 0 && bytes[length - 1] == (byte)'\r') length--;
            return Latin1.GetString(bytes, 0, length);
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes (the request body,
        /// once <c>Content-Length</c> is known) -- draining whatever's left
        /// in the internal buffer first, then reading directly from the
        /// underlying stream for the rest. Throws
        /// <see cref="EndOfStreamException"/> if the connection closes
        /// before <paramref name="count"/> bytes arrive.
        /// </summary>
        public async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
        {
            var result = new byte[count];
            var written = 0;
            while (written < count)
            {
                if (_pos < _len)
                {
                    var take = Math.Min(_len - _pos, count - written);
                    Buffer.BlockCopy(_buffer, _pos, result, written, take);
                    _pos += take;
                    written += take;
                    continue;
                }

                if (!await FillAsync(ct).ConfigureAwait(false))
                {
                    throw new EndOfStreamException("Connection closed before the declared Content-Length was fully received.");
                }
            }
            return result;
        }
    }
}
