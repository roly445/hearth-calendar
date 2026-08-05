using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.CalDav;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;

namespace HearthCalendar.Tests.Server;

public sealed class GetCalendarObjectAsyncTests : CalDavEndpointTestBase
{
    [Fact]
    public async Task Caldav_read_credential_cannot_read_unlisted_virtual_calendar()
    {
        var store = new RecordingCalDavStore();
        store.ApprovedEvents.Add(AdultAEvent(
            "Dentist for Adult A",
            new DateOnly(2026, 8, 1),
            new TimeOnly(9, 0),
            new TimeOnly(9, 30)));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var response = await client.SendAsync(Report(
            "/caldav/calendars/adult-b/",
            """
            <C:calendar-query xmlns:C="urn:ietf:params:xml:ns:caldav" />
            """));

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            QueryCount = store.Queries.Count
        });
    }

    [Fact]
    public async Task Read_only_caldav_get_returns_approved_virtual_calendar_event()
    {
        var store = new RecordingCalDavStore();
        var approved = AdultAEvent(
            "Dentist for Adult A",
            new DateOnly(2026, 8, 1),
            new TimeOnly(9, 0),
            new TimeOnly(9, 30));
        store.ApprovedEvents.Add(approved);
        store.ApprovedEvents.Add(approved with
        {
            Id = CalendarEventId.New(),
            Title = "Staged Adult A appointment",
            ReviewStatus = ReviewStatus.Staged
        });
        store.ApprovedEvents.Add(approved with
        {
            Id = CalendarEventId.New(),
            Title = "Rejected Adult A appointment",
            ReviewStatus = ReviewStatus.Rejected
        });
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var response = await client.GetAsync($"/caldav/calendars/adult-a/{approved.Id.Value}.ics");
        var content = await response.Content.ReadAsStringAsync();
        var parsed = IcsAssertions.Parse(content);

        var parsedEvent = Assert.Single(parsed.Events);
        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            Calendar = parsed.CalendarProperties,
            Event = NormalizeIcsEvent(parsedEvent.Properties)
        });
    }
}
