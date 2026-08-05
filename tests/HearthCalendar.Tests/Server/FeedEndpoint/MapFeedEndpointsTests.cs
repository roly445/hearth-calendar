using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;

namespace HearthCalendar.Tests.Server;

public sealed class MapFeedEndpointsTests : FeedEndpointTestBase
{
    [Fact]
    public async Task Feed_requires_valid_feed_token()
    {
        var store = new RecordingFeedStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var missing = await client.GetAsync("/feeds/adult-a.ics");
        var invalid = await client.GetAsync("/feeds/adult-a.ics?token=wrong-token");

        await Verifier.Verify(new
        {
            Missing = EndpointSnapshot.ForResponse(missing),
            Invalid = EndpointSnapshot.ForResponse(invalid)
        });
    }

    [Fact]
    public async Task Feed_token_for_one_calendar_cannot_read_another_calendar()
    {
        var store = new RecordingFeedStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/feeds/family.ics?token={AdultAToken}");

        await Verifier.Verify(EndpointSnapshot.ForResponse(response));
    }

    [Fact]
    public async Task Feed_uses_marten_backed_approved_event_query_for_requested_calendar()
    {
        var store = new RecordingFeedStore();
        store.ApprovedEvents.Add(AdultAEvent(
            "Dentist for Adult A",
            Today,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30)));
        store.ApprovedEvents.Add(FamilyAllDayEvent("Family planning", Today));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdultAToken);

        var response = await client.GetAsync("/feeds/adult-a.ics");
        var content = await response.Content.ReadAsStringAsync();
        var parsed = IcsAssertions.Parse(content);

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            Query = store.Queries.Single(),
            EventSummaries = parsed.Events
                .Select(item => item.Properties["SUMMARY"])
                .Order()
                .ToArray()
        });
    }

    [Fact]
    public async Task Malformed_authorization_header_does_not_fall_back_to_query_token()
    {
        var store = new RecordingFeedStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Token malformed");

        var response = await client.GetAsync($"/feeds/adult-a.ics?token={AdultAToken}");

        await Verifier.Verify(EndpointSnapshot.ForResponse(response));
    }
}
