using System.Net;
using System.Net.Http.Json;
using HearthCalendar.Client.Contracts.Auth;

namespace HearthCalendar.Client.Services;

public sealed class AdminSessionClient(HttpClient httpClient)
{
    public async Task<AdminSessionResponse> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("/api/admin/session", cancellationToken);

        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<AdminSessionResponse>(cancellationToken) ??
                new AdminSessionResponse(false)
            : new AdminSessionResponse(false);
    }

    public async Task<AdminLoginResponse?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/admin/login",
            new AdminLoginRequest(username, password),
            cancellationToken);

        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<AdminLoginResponse>(cancellationToken)
            : null;
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        httpClient.PostAsync("/api/admin/logout", null, cancellationToken);
}
