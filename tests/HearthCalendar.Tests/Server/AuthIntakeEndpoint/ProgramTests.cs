using HearthCalendar.Server.Auth;
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
}
