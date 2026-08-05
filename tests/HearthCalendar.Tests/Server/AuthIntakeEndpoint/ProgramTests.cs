using HearthCalendar.Server.Auth;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace HearthCalendar.Tests.Server;

public sealed class ProgramTests : AuthIntakeEndpointTestBase
{
    [Fact]
    public async Task Admin_endpoint_requires_authentication()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Write_token_cannot_access_admin_endpoint()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Valid_admin_login_creates_session_cookie_for_protected_admin_endpoints()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/admin/login",
            new AdminLoginRequest(AdminUsername, AdminPassword));
        var session = await client.GetAsync("/api/admin/session");
        var body = await session.Content.ReadFromJsonAsync<AdminSessionResponse>();

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.True(body?.IsAuthenticated);
        Assert.Equal("Calendar Admin", body?.DisplayName);
    }

    [Fact]
    public async Task Invalid_admin_login_fails_without_session_cookie()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/admin/login",
            new AdminLoginRequest(AdminUsername, "wrong-password"));
        var session = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task Logout_clears_admin_session()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/admin/login",
            new AdminLoginRequest(AdminUsername, AdminPassword));
        var logout = await client.PostAsync("/api/admin/logout", null);
        var session = await client.GetAsync("/api/admin/session");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }
}
