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

public sealed class MapIntakeEndpointsTests : AuthIntakeEndpointTestBase
{
    [Fact]
    public async Task Feed_token_cannot_submit_intent()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FeedToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new IntakeEventRequest("Family calendar planning"));

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        });
    }

    [Fact]
    public async Task Invalid_source_mode_is_rejected_without_storing_intent()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new
            {
                RawText = "Family calendar planning",
                SourceMode = 999
            });

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        });
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-token", HttpStatusCode.Unauthorized)]
    public async Task Missing_or_invalid_token_is_rejected_without_storing_intent(
        string? token,
        HttpStatusCode expectedStatusCode)
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new IntakeEventRequest("Family calendar planning"));

        await Verifier.Verify(new
        {
            ExpectedStatusCode = expectedStatusCode,
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        }).UseParameters(token ?? "missing", expectedStatusCode);
    }

    [Fact]
    public async Task Valid_home_assistant_token_can_submit_home_assistant_intent()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/home-assistant/event",
            new IntakeEventRequest("Adult A appointment", Date: new DateOnly(2026, 8, 2)));

        await Verifier.Verify(new
        {
            response.StatusCode,
            StoredIntents = store.Intents.Select(DescribeIntent),
            Audits = store.Audits.Select(DescribeAudit)
        });
    }

    [Fact]
    public async Task Valid_intake_token_can_submit_generic_intent_and_audit_without_raw_secret()
    {
        var store = new RecordingHearthCalendarStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", WriteToken);

        var response = await client.PostAsJsonAsync(
            "/api/intake/event",
            new IntakeEventRequest(
                "Family calendar planning",
                ReviewSourceMode.Passive,
                new DateOnly(2026, 8, 1),
                new TimeOnly(10, 0),
                new TimeOnly(11, 0)));

        await Verifier.Verify(new
        {
            response.StatusCode,
            Body = await response.Content.ReadFromJsonAsync<IntakeEventResponse>(),
            StoredIntents = store.Intents.Select(DescribeIntent),
            Audits = store.Audits.Select(DescribeAudit)
        });
    }
}
