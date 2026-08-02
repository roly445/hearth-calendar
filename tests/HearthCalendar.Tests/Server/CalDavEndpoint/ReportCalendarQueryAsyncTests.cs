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

public sealed class ReportCalendarQueryAsyncTests : CalDavEndpointTestBase
{
    [Fact]
    public async Task Calendar_query_report_keeps_same_day_non_midnight_end_range()
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
            "/caldav/calendars/adult-a/",
            """
            <C:calendar-query xmlns:C="urn:ietf:params:xml:ns:caldav">
              <C:filter>
                <C:comp-filter name="VCALENDAR">
                  <C:comp-filter name="VEVENT">
                    <C:time-range start="20260801T090000Z" end="20260801T120000Z" />
                  </C:comp-filter>
                </C:comp-filter>
              </C:filter>
            </C:calendar-query>
            """));

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Equal(new DateOnly(2026, 8, 1), store.Queries.Single().From);
        Assert.Equal(new DateOnly(2026, 8, 1), store.Queries.Single().To);
    }

    [Fact]
    public async Task Calendar_query_report_returns_approved_events_in_requested_range()
    {
        var store = new RecordingCalDavStore();
        store.ApprovedEvents.Add(AdultAEvent(
            "Before range",
            new DateOnly(2026, 7, 31),
            new TimeOnly(9, 0),
            new TimeOnly(9, 30)));
        store.ApprovedEvents.Add(AdultAEvent(
            "Dentist for Adult A",
            new DateOnly(2026, 8, 1),
            new TimeOnly(9, 0),
            new TimeOnly(9, 30)));
        store.ApprovedEvents.Add(BirthdayEvent("Adult B birthday", new DateOnly(2026, 8, 2)));
        store.ApprovedEvents.Add(AdultAEvent(
            "After range",
            new DateOnly(2026, 8, 3),
            new TimeOnly(9, 0),
            new TimeOnly(9, 30)));
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var response = await client.SendAsync(Report(
            "/caldav/calendars/combined/",
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <C:calendar-query xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
              <D:prop>
                <D:getetag />
                <C:calendar-data />
              </D:prop>
              <C:filter>
                <C:comp-filter name="VCALENDAR">
                  <C:comp-filter name="VEVENT">
                    <C:time-range start="20260801T000000Z" end="20260803T000000Z" />
                  </C:comp-filter>
                </C:comp-filter>
              </C:filter>
            </C:calendar-query>
            """));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Equal(new DateOnly(2026, 8, 1), store.Queries.Single().From);
        Assert.Equal(new DateOnly(2026, 8, 2), store.Queries.Single().To);
        await Verifier.Verify(NormalizeReportXml(document));
    }
}
