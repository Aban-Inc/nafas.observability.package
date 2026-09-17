namespace Nafas.Observability.Server.Internal
{
    /// <summary>
    /// The standard HTTP reason phrase for a status code, for the status
    /// line (<c>HTTP/1.1 200 OK</c>). Purely cosmetic -- every real client
    /// (browsers, <c>fetch</c>, <c>EventSource</c>) acts on the numeric code
    /// alone -- so an unrecognized code falls back to a generic placeholder
    /// rather than needing an exhaustive table.
    /// </summary>
    internal static class ReasonPhrases
    {
        public static string Get(int statusCode) => statusCode switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            204 => "No Content",
            206 => "Partial Content",
            301 => "Moved Permanently",
            302 => "Found",
            304 => "Not Modified",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            411 => "Length Required",
            412 => "Precondition Failed",
            416 => "Range Not Satisfiable",
            500 => "Internal Server Error",
            501 => "Not Implemented",
            503 => "Service Unavailable",
            _ => "Status",
        };
    }
}
