using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HearthCalendar.Client.Services;

public sealed class CookieCredentialHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        return base.SendAsync(request, cancellationToken);
    }
}
