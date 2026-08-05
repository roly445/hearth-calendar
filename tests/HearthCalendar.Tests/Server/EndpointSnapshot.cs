using System.Net.Http.Headers;

namespace HearthCalendar.Tests.Server;

public static class EndpointSnapshot
{
    public static object ForResponse(HttpResponseMessage response) => new
    {
        response.StatusCode,
        ContentType = response.Content.Headers.ContentType?.MediaType,
        Allow = response.Content.Headers.Allow.ToString(),
        Dav = HeaderValue(response.Headers, "DAV"),
        WwwAuthenticate = response.Headers.WwwAuthenticate.ToString(),
        HasETag = response.Headers.ETag is not null,
        HasLocation = response.Headers.Location is not null
    };

    public static object ForResponseWithStableETag(HttpResponseMessage response) => new
    {
        response.StatusCode,
        ContentType = response.Content.Headers.ContentType?.MediaType,
        Allow = response.Content.Headers.Allow.ToString(),
        Dav = HeaderValue(response.Headers, "DAV"),
        WwwAuthenticate = response.Headers.WwwAuthenticate.ToString(),
        ETag = response.Headers.ETag is null ? null : "stable-etag",
        HasLocation = response.Headers.Location is not null
    };

    private static string HeaderValue(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values)
            ? string.Join(",", values)
            : string.Empty;
}
