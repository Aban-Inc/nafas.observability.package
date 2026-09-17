using System.Text;

namespace Nafas.Observability.Server.Internal
{
    /// <summary>
    /// HTTP/1.1 request lines and header field names/values are defined over
    /// a byte-for-byte-preserving charset (RFC 7230 uses US-ASCII for the
    /// grammar itself and treats header VALUES as opaque octets, historically
    /// ISO-8859-1/Latin-1 by convention) -- NOT UTF-8. Latin-1 maps every
    /// byte 0-255 directly to the Unicode code point of the same number, so
    /// this needs no <see cref="Encoding"/> lookup (some platforms don't ship
    /// every codepage) and, unlike UTF-8, never throws or substitutes on
    /// arbitrary input -- appropriate for parsing untrusted bytes off a raw
    /// socket. Our own SPA client only ever sends plain ASCII here anyway
    /// (a subset of Latin-1), so this is never actually exercised beyond
    /// ASCII in practice; it's just the correct/robust choice regardless.
    /// </summary>
    internal static class Latin1
    {
        public static string GetString(byte[] buffer, int offset, int count)
        {
            var chars = new char[count];
            for (var i = 0; i < count; i++)
            {
                chars[i] = (char)buffer[offset + i];
            }
            return new string(chars);
        }

        public static byte[] GetBytes(string value)
        {
            var bytes = new byte[value.Length];
            for (var i = 0; i < value.Length; i++)
            {
                bytes[i] = unchecked((byte)value[i]);
            }
            return bytes;
        }
    }
}
