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

public sealed class IcsFeedWriterWriteTests : FeedEndpointTestBase
{
    [Fact]
    public async Task Feed_endpoint_returns_approved_events_as_ics()
    {
        var store = new RecordingFeedStore();
        store.ApprovedEvents.Add(AdultAEvent(
            "Dentist for Adult A",
            Today,
            new TimeOnly(9, 0),
            new TimeOnly(9, 30)));
        store.ApprovedEvents.Add(BirthdayEvent("Adult B birthday", Today.AddDays(1)));
        store.ApprovedEvents.Add(FamilyAllDayEvent("Family planning", Today.AddDays(2)));
        store.ApprovedEvents.Add(AdultAEvent(
            "Staged Adult A appointment",
            Today,
            new TimeOnly(12, 0),
            new TimeOnly(12, 30)) with
            {
                ReviewStatus = ReviewStatus.Staged
            });
        store.ApprovedEvents.Add(AdultAEvent(
            "Rejected Adult A appointment",
            Today,
            new TimeOnly(13, 0),
            new TimeOnly(13, 30)) with
            {
                ReviewStatus = ReviewStatus.Rejected
            });
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/feeds/combined.ics?token={CombinedToken}");
        var content = await response.Content.ReadAsStringAsync();
        var parsed = IcsAssertions.Parse(content);

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            EventCount = parsed.Events.Count,
            Calendar = parsed.CalendarProperties,
            Events = parsed.Events.Select(item => NormalizeEvent(item.Properties))
        });
    }
}
