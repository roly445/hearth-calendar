using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Xml.Linq;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.CalDav;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Contracts.Ui;
using HearthCalendar.Shared.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HearthCalendar.Tests.Server;

public sealed class CalDavEndpointTests
{
    private const string CalDavUser = "caldav-app";
    private const string CalDavPassword = "test-caldav-app-password";
    private const string CalDavReadUser = "caldav-read-app";
    private const string CalDavReadPassword = "test-caldav-read-app-password";
    private const string WriteToken = "test-write-token";
    private const string FeedToken = "test-feed-token";

    [Fact]
    public async Task Discovery_requires_caldav_basic_authentication_challenge()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(PropFind("/caldav/"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Basic realm=\"Hearth Calendar CalDAV\"", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Options_advertises_caldav_discovery_methods()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var root = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/caldav/"));
        var smartInbox = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Options,
            "/caldav/calendars/smart-inbox/"));
        var smartInboxArchive = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Options,
            "/caldav/calendars/smart-inbox-archive/"));

        Assert.Equal(HttpStatusCode.NoContent, root.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND", root.Content.Headers.Allow.ToString());
        Assert.Equal("1, 3, calendar-access", root.Headers.GetValues("DAV").Single());
        Assert.Equal(HttpStatusCode.NoContent, smartInbox.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND, PUT", smartInbox.Content.Headers.Allow.ToString());
        Assert.Equal("1, 3, calendar-access", smartInbox.Headers.GetValues("DAV").Single());
        Assert.Equal(HttpStatusCode.NoContent, smartInboxArchive.StatusCode);
        Assert.Equal("OPTIONS, PROPFIND", smartInboxArchive.Content.Headers.Allow.ToString());
    }

    [Theory]
    [InlineData(WriteToken)]
    [InlineData(FeedToken)]
    public async Task Bearer_tokens_cannot_use_caldav_discovery(string token)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(PropFind("/caldav/calendars/"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_caldav_credential_can_discover_but_not_write()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var discovery = await client.SendAsync(PropFind("/caldav/calendars/"));
        var write = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal((HttpStatusCode)207, discovery.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Headers.ETag);
        var parsedEvent = Assert.Single(parsed.Events);
        await Verifier.Verify(new
        {
            Calendar = parsed.CalendarProperties,
            Event = NormalizeIcsEvent(parsedEvent.Properties)
        });
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(store.Queries);
    }

    [Fact]
    public async Task Propfind_root_returns_service_discovery_multistatus()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1, 3, calendar-access", response.Headers.GetValues("DAV").Single());
        await Verifier.Verify(NormalizeDiscoveryXml(document));
    }

    [Fact]
    public async Task Propfind_calendars_returns_smart_inbox_and_virtual_calendar_metadata()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/", depth: "1"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        await Verifier.Verify(NormalizeDiscoveryXml(document));
    }

    [Fact]
    public async Task Propfind_smart_inbox_marks_calendar_writable()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/smart-inbox/", depth: "0"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var discovery = NormalizeDiscoveryXml(document);

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/smart-inbox/" &&
            item.Privileges.SequenceEqual(["write"]));
    }

    [Fact]
    public async Task Propfind_calendar_privileges_are_scoped_to_caldav_credential()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavReadUser, CalDavReadPassword);

        var response = await client.SendAsync(PropFind("/caldav/calendars/", depth: "1"));
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var discovery = NormalizeDiscoveryXml(document);

        Assert.Equal((HttpStatusCode)207, response.StatusCode);
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/adult-a/" &&
            item.Privileges.SequenceEqual(["read"]));
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/combined/" &&
            item.Privileges.SequenceEqual(["read"]));
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/smart-inbox/" &&
            item.Privileges.Count == 0);
        Assert.Contains(discovery, item =>
            item.Href == "/caldav/calendars/adult-b/" &&
            item.Privileges.Count == 0);
    }

    [Fact]
    public async Task Smart_inbox_put_creates_caldav_event_intent_and_audit()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                VERSION:2.0
                BEGIN:VEVENT
                UID:family-planning@example.invalid
                SUMMARY:Family planning
                DTSTART:20260901T100000Z
                DTEND:20260901T110000Z
                END:VEVENT
                END:VCALENDAR
                """));

        await Verifier.Verify(new
        {
            response.StatusCode,
            Location = response.Headers.Location?.ToString(),
            ETag = response.Headers.ETag?.ToString(),
            Body = await response.Content.ReadFromJsonAsync<IntakeEventResponse>(),
            StoredIntents = store.Intents.Select(DescribeIntent),
            Audits = store.Audits.Select(DescribeAudit),
            Decisions = store.Decisions.Select(DescribeDecision),
            ApprovedEvents = store.ApprovedEvents.Select(DescribeEvent),
            Objects = store.Objects.Values.Select(DescribeObject)
        });
    }

    [Fact]
    public async Task Smart_inbox_put_publishes_after_review_outcome_persists()
    {
        var store = new RecordingCalDavStore();
        var notifier = new RecordingNotifier(store);
        await using var factory = CreateFactory(store, notifier);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(notifier.StoreHadPersistedDecisionWhenPublished);
        Assert.Contains(notifier.Published, notification => notification.Type == CalendarUiNotifications.ReviewQueueChanged);
        Assert.Contains(notifier.Published, notification => notification.Type == CalendarUiNotifications.CalendarEventsChanged);
    }

    [Fact]
    public async Task Ambiguous_smart_inbox_put_becomes_staged_review_item_not_approved_event()
    {
        var store = new RecordingCalDavStore();
        var notifier = new RecordingNotifier(store);
        await using var factory = CreateFactory(store, notifier);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/school-trip.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:School trip
                DTSTART:20260901T100000Z
                DTEND:20260901T110000Z
                END:VEVENT
                END:VCALENDAR
                """));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var decision = Assert.Single(store.Decisions);
        Assert.Equal(ReviewStatus.Staged, decision.Status);
        Assert.Contains(decision.Reasons, reason => reason.Code == DecisionReasonCode.AmbiguousIntent);
        Assert.Empty(store.ApprovedEvents);
        Assert.Contains(store.Audits, audit => audit.Action == AuditAction.EventStaged);
        Assert.Contains(notifier.Published, notification => notification.Type == CalendarUiNotifications.ReviewQueueChanged);
        Assert.DoesNotContain(notifier.Published, notification => notification.Type == CalendarUiNotifications.CalendarEventsChanged);
    }

    [Fact]
    public async Task Caldav_review_uses_plug_in_ai_provider_as_advisory()
    {
        var store = new RecordingCalDavStore();
        var provider = new CountingAiReviewProvider(new AiReviewSuggestion(
            AiReviewSuggestionId.New(),
            "stub",
            "stub-model",
            "Adult A appointment",
            VirtualCalendar.AdultA,
            [KnownPeople.AdultA.Id],
            null,
            null,
            0.95m,
            ["Matched placeholder details."],
            DateTimeOffset.UtcNow));
        await using var factory = CreateFactory(store, aiReviewProvider: provider);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/appointment.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Appointment
                DTSTART:20260901T100000Z
                DTEND:20260901T110000Z
                END:VEVENT
                END:VCALENDAR
                """));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, provider.Calls);
        var decision = Assert.Single(store.Decisions);
        Assert.Equal(DecisionMode.AssistedByAi, decision.Mode);
        Assert.Equal(ReviewStatus.Approved, decision.Status);
        Assert.Single(store.ApprovedEvents);
    }

    [Fact]
    public async Task Identical_smart_inbox_retry_does_not_call_ai_provider_again()
    {
        var store = new RecordingCalDavStore();
        var provider = new CountingAiReviewProvider(new AiReviewSuggestion(
            AiReviewSuggestionId.New(),
            "stub",
            "stub-model",
            "Adult A appointment",
            VirtualCalendar.AdultA,
            [KnownPeople.AdultA.Id],
            null,
            null,
            0.95m,
            ["Matched placeholder details."],
            DateTimeOffset.UtcNow));
        await using var factory = CreateFactory(store, aiReviewProvider: provider);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/appointment.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Appointment
                DTSTART:20260901T100000Z
                DTEND:20260901T110000Z
                END:VEVENT
                END:VCALENDAR
                """));
        var retry = await client.PutAsync(
            "/caldav/calendars/smart-inbox/appointment.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Appointment
                DTSTART:20260901T100000Z
                DTEND:20260901T110000Z
                END:VEVENT
                END:VCALENDAR
                """));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal(1, provider.Calls);
        Assert.Single(store.Decisions);
    }

    [Fact]
    public async Task Repeated_identical_smart_inbox_put_reuses_existing_intent_and_etag()
    {
        var store = new RecordingCalDavStore();
        var notifier = new RecordingNotifier(store);
        await using var factory = CreateFactory(store, notifier);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var retry = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Equal(first.Headers.ETag, retry.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Equal(2, store.Audits.Count);
        Assert.Single(store.Decisions);
        Assert.Single(store.ApprovedEvents);
        Assert.Single(store.Objects);
        Assert.Equal(2, notifier.Published.Count);
    }

    [Fact]
    public async Task Changed_smart_inbox_put_replaces_object_metadata_and_creates_new_intent()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var changed = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Updated family planning
                DTSTART:20260901T120000Z
                DTEND:20260901T130000Z
                END:VEVENT
                END:VCALENDAR
                """));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(first.Headers.ETag, changed.Headers.ETag);
        Assert.Equal(2, store.Intents.Count);
        Assert.Equal(5, store.Audits.Count);
        Assert.Equal(2, store.Decisions.Count);
        Assert.Single(store.ApprovedEvents);
        Assert.Contains(store.Decisions, decision => decision.Status == ReviewStatus.Rejected);
        Assert.Contains(store.Decisions, decision => decision.Status == ReviewStatus.Approved);
        var storedObject = Assert.Single(store.Objects.Values);
        Assert.Equal(store.Intents[1].Id, storedObject.IntentId);
        Assert.Equal(changed.Headers.ETag?.ToString(), storedObject.ETag);
    }

    [Fact]
    public async Task Stale_if_match_is_rejected_without_creating_intent_or_audit()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var changed = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Updated family planning
                DTSTART:20260801T120000Z
                DTEND:20260801T130000Z
                END:VEVENT
                END:VCALENDAR
                """)
        };
        changed.Headers.TryAddWithoutValidation("If-Match", "\"stale-etag\"");

        var response = await client.SendAsync(changed);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(first.Headers.ETag, response.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Equal(2, store.Audits.Count);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task If_none_match_star_prevents_overwriting_existing_object()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var retry = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent(BasicIcs())
        };
        retry.Headers.TryAddWithoutValidation("If-None-Match", "*");

        var response = await client.SendAsync(retry);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(first.Headers.ETag, response.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Equal(2, store.Audits.Count);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task If_match_star_requires_existing_object()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var write = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent(BasicIcs())
        };
        write.Headers.TryAddWithoutValidation("If-Match", "*");

        var response = await client.SendAsync(write);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task If_none_match_matching_etag_prevents_overwrite()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var first = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));
        var changed = new HttpRequestMessage(HttpMethod.Put, "/caldav/calendars/smart-inbox/family-planning.ics")
        {
            Content = IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Updated family planning
                DTSTART:20260801T120000Z
                DTEND:20260801T130000Z
                END:VEVENT
                END:VCALENDAR
                """)
        };
        changed.Headers.TryAddWithoutValidation("If-None-Match", first.Headers.ETag?.ToString());

        var response = await client.SendAsync(changed);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(first.Headers.ETag, response.Headers.ETag);
        Assert.Single(store.Intents);
        Assert.Equal(2, store.Audits.Count);
        Assert.Single(store.Objects);
    }

    [Fact]
    public async Task Smart_inbox_item_ids_remain_case_sensitive()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var upper = await client.PutAsync(
            "/caldav/calendars/smart-inbox/Family-Planning.ics",
            IcsContent(BasicIcs()));
        var lower = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.Created, upper.StatusCode);
        Assert.Equal(HttpStatusCode.Created, lower.StatusCode);
        Assert.Equal(2, store.Intents.Count);
        Assert.Equal(4, store.Audits.Count);
        Assert.Equal(2, store.Decisions.Count);
        Assert.Equal(2, store.Objects.Count);
        Assert.Contains("smart-inbox/Family-Planning", store.Objects.Keys);
        Assert.Contains("smart-inbox/family-planning", store.Objects.Keys);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("wrong-caldav-password", HttpStatusCode.Unauthorized)]
    public async Task Smart_inbox_put_requires_valid_caldav_app_password(
        string? password,
        HttpStatusCode expectedStatusCode)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        if (password is not null)
        {
            client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, password);
        }

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Theory]
    [InlineData(WriteToken)]
    [InlineData(FeedToken)]
    public async Task Bearer_tokens_cannot_write_caldav_calendar_objects(string token)
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Unsupported_caldav_calendar_is_not_writable()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/adult-a/family-planning.ics",
            IcsContent(BasicIcs()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Supplied_invalid_dtend_is_rejected_without_storing_intent()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent("""
                BEGIN:VCALENDAR
                BEGIN:VEVENT
                SUMMARY:Family planning
                DTSTART:20260801T100000Z
                DTEND:not-a-date
                END:VEVENT
                END:VCALENDAR
                """));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task Oversized_caldav_body_is_rejected_without_storing_intent()
    {
        var store = new RecordingCalDavStore();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Basic(CalDavUser, CalDavPassword);
        var oversizedBody = BasicIcs() + new string('X', 70 * 1024);

        var response = await client.PutAsync(
            "/caldav/calendars/smart-inbox/family-planning.ics",
            IcsContent(oversizedBody));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(store.Intents);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public void Parser_accepts_all_day_vevent_without_faking_midnight_times()
    {
        var parsed = CalDavEventParser.Parse("""
            BEGIN:VCALENDAR
            BEGIN:VEVENT
            SUMMARY:Adult B birthday
            DTSTART;VALUE=DATE:20260725
            END:VEVENT
            END:VCALENDAR
            """);

        Assert.NotNull(parsed);
        Assert.Equal("Adult B birthday", parsed.Summary);
        Assert.Equal(new DateOnly(2026, 7, 25), parsed.Date);
        Assert.Null(parsed.StartTime);
        Assert.Null(parsed.EndTime);
    }

    private static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory(
        RecordingCalDavStore store,
        RecordingNotifier? notifier = null,
        IAiReviewProvider? aiReviewProvider = null) =>
        new WebApplicationFactory<HearthCalendar.Server.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = "Host=localhost;Database=hearth_calendar_test",
                        ["Database:SchemaName"] = "hearth_calendar_test",
                        ["Auth:ClientTokens:0:Name"] = "home-assistant",
                        ["Auth:ClientTokens:0:SecretHash"] = HearthCalendarSecretHasher.Hash(WriteToken),
                        ["Auth:ClientTokens:0:Scopes:0"] = HearthCalendarAuth.IntakeWriteScope,
                        ["Auth:FeedTokens:0:Name"] = "adult-a-feed",
                        ["Auth:FeedTokens:0:TokenHash"] = HearthCalendarSecretHasher.Hash(FeedToken),
                        ["Auth:FeedTokens:0:AllowedCalendars:0"] = VirtualCalendar.AdultA.ToString(),
                        ["Auth:FeedTokens:0:Scopes:0"] = HearthCalendarAuth.FeedReadScope,
                        ["Auth:CalDavCredentials:0:Name"] = CalDavUser,
                        ["Auth:CalDavCredentials:0:SecretHash"] = HearthCalendarSecretHasher.Hash(CalDavPassword),
                        ["Auth:CalDavCredentials:0:WritableCalendars:0"] = "smart-inbox",
                        ["Auth:CalDavCredentials:0:Scopes:0"] = HearthCalendarAuth.CalDavWriteScope,
                        ["Auth:CalDavCredentials:1:Name"] = CalDavReadUser,
                        ["Auth:CalDavCredentials:1:SecretHash"] = HearthCalendarSecretHasher.Hash(CalDavReadPassword),
                        ["Auth:CalDavCredentials:1:ReadableCalendars:0"] = "adult-a",
                        ["Auth:CalDavCredentials:1:ReadableCalendars:1"] = "combined",
                        ["Auth:CalDavCredentials:1:Scopes:0"] = HearthCalendarAuth.CalDavReadScope
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHearthCalendarStore>();
                    services.RemoveAll<ICalendarUpdateNotifier>();
                    services.RemoveAll<IAiReviewProvider>();
                    services.AddSingleton<IHearthCalendarStore>(store);
                    services.AddSingleton<ICalendarUpdateNotifier>(notifier ?? new RecordingNotifier(store));
                    services.AddSingleton(aiReviewProvider ?? NoOpAiReviewProvider.Instance);
                });
            });

    private static AuthenticationHeaderValue Basic(string user, string password)
    {
        var bytes = Encoding.UTF8.GetBytes($"{user}:{password}");

        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    private static StringContent IcsContent(string content) =>
        new(content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal), Encoding.UTF8, "text/calendar");

    private static HttpRequestMessage PropFind(string uri, string depth = "0")
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), uri);
        request.Headers.TryAddWithoutValidation("Depth", depth);

        return request;
    }

    private static HttpRequestMessage Report(string uri, string body)
    {
        var request = new HttpRequestMessage(new HttpMethod("REPORT"), uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml")
        };
        request.Headers.TryAddWithoutValidation("Depth", "1");

        return request;
    }

    private static IReadOnlyList<CalDavDiscoveryResponse> NormalizeDiscoveryXml(XDocument document)
    {
        XNamespace dav = "DAV:";
        XNamespace calDav = "urn:ietf:params:xml:ns:caldav";

        return document
            .Descendants(dav + "response")
            .Select(response => new CalDavDiscoveryResponse(
                response.Element(dav + "href")?.Value ?? string.Empty,
                response.Descendants(dav + "status").SingleOrDefault()?.Value ?? string.Empty,
                response.Descendants(dav + "displayname").SingleOrDefault()?.Value ?? string.Empty,
                response.Descendants(dav + "current-user-principal").Elements(dav + "href").SingleOrDefault()?.Value ?? string.Empty,
                response.Descendants(calDav + "calendar-home-set").Elements(dav + "href").SingleOrDefault()?.Value ?? string.Empty,
                response
                    .Descendants(dav + "resourcetype")
                    .Elements()
                    .Select(element => element.Name.LocalName)
                    .Order()
                    .ToArray(),
                response
                    .Descendants(calDav + "comp")
                    .Select(element => element.Attribute("name")?.Value)
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .Order()
                    .ToArray(),
                response
                    .Descendants(dav + "privilege")
                    .Elements()
                    .Select(element => element.Name.LocalName)
                    .Order()
                    .ToArray()))
            .OrderBy(response => response.Href)
            .ToArray();
    }

    private static IReadOnlyList<CalDavReportResponse> NormalizeReportXml(XDocument document)
    {
        XNamespace dav = "DAV:";
        XNamespace calDav = "urn:ietf:params:xml:ns:caldav";

        return document
            .Descendants(dav + "response")
            .Select(response =>
            {
                var calendarData = response.Descendants(calDav + "calendar-data").Single().Value;
                var parsed = IcsAssertions.Parse(calendarData);
                var parsedEvent = Assert.Single(parsed.Events);

                return new CalDavReportResponse(
                    NormalizeCalDavHref(response.Element(dav + "href")?.Value),
                    string.IsNullOrWhiteSpace(response.Descendants(dav + "getetag").SingleOrDefault()?.Value)
                        ? string.Empty
                        : "stable-etag",
                    NormalizeIcsEvent(parsedEvent.Properties));
            })
            .OrderBy(response => response.Event["SUMMARY"])
            .ToArray();
    }

    private static string NormalizeCalDavHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var lastSlash = href.LastIndexOf("/", StringComparison.Ordinal);

        return lastSlash < 0 ? href : href[..(lastSlash + 1)] + "stable-event.ics";
    }

    private static IReadOnlyDictionary<string, string> NormalizeIcsEvent(IReadOnlyDictionary<string, string> properties)
    {
        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            normalized[key] = value;
        }

        if (normalized.ContainsKey("UID"))
        {
            normalized["UID"] = "stable-uid@hearth-calendar";
        }

        return normalized;
    }

    private sealed record CalDavDiscoveryResponse(
        string Href,
        string Status,
        string DisplayName,
        string PrincipalHref,
        string CalendarHomeSet,
        IReadOnlyList<string> ResourceTypes,
        IReadOnlyList<string> Components,
        IReadOnlyList<string> Privileges);

    private sealed record CalDavReportResponse(
        string Href,
        string ETag,
        IReadOnlyDictionary<string, string> Event);

    private static string BasicIcs() =>
        """
        BEGIN:VCALENDAR
        BEGIN:VEVENT
        SUMMARY:Family planning
        DTSTART:20260901T100000Z
        DTEND:20260901T110000Z
        END:VEVENT
        END:VCALENDAR
        """;

    private static CalendarEvent AdultAEvent(
        string title,
        DateOnly date,
        TimeOnly? startTime,
        TimeOnly? endTime) =>
        CalendarEvent.Approved(
            CalendarEventId.New(),
            title,
            new EventTime(date, startTime, endTime, startTime is null && endTime is null),
            VirtualCalendar.AdultA,
            EventCategory.Personal,
            BusyStatus.Busy,
            [new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy)],
            CalendarSource.Test);

    private static CalendarEvent BirthdayEvent(string title, DateOnly date) =>
        CalendarEvent.Approved(
            CalendarEventId.New(),
            title,
            new EventTime(date, null, null, true),
            VirtualCalendar.Events,
            EventCategory.Birthday,
            BusyStatus.Free,
            [new Participant(KnownPeople.AdultB, ParticipationRole.Attendee, BusyStatus.Free)],
            CalendarSource.Test,
            new RecurrenceRule(RecurrenceFrequency.Yearly));

    private static object DescribeIntent(EventIntent intent) => new
    {
        HasId = intent.Id.Value != Guid.Empty,
        Source = intent.Source.ToString(),
        SourceMode = intent.SourceMode.ToString(),
        intent.RawText,
        Payload = intent.Payload is null
            ? null
            : new
            {
                Date = intent.Payload.Date?.ToString("O"),
                StartTime = intent.Payload.StartTime?.ToString("HH:mm:ss"),
                EndTime = intent.Payload.EndTime?.ToString("HH:mm:ss")
            },
        HasSubmittedAt = intent.SubmittedAt != default,
        SubmittedBy = intent.SubmittedBy.Id
    };

    private static object DescribeAudit(AuditEntry audit) => new
    {
        Action = audit.Action.ToString(),
        Actor = audit.Actor.Id,
        HasOccurredAt = audit.OccurredAt != default,
        audit.Summary,
        HasIntentLink = audit.IntentId is not null,
        audit.Metadata,
        ContainsRawCalDavPassword = ContainsValue(audit.Metadata, CalDavPassword),
        ContainsRawWriteToken = ContainsValue(audit.Metadata, WriteToken),
        ContainsRawFeedToken = ContainsValue(audit.Metadata, FeedToken)
    };

    private static object DescribeDecision(ReviewDecision decision) => new
    {
        Status = decision.Status.ToString(),
        Mode = decision.Mode.ToString(),
        Event = decision.Event is null ? null : DescribeEvent(decision.Event),
        Reasons = decision.Reasons.Select(reason => new
        {
            Code = reason.Code.ToString(),
            reason.Message
        }),
        Clashes = decision.Clashes.Select(clash => new
        {
            Severity = clash.Severity.ToString(),
            clash.Summary,
            AffectedPeople = clash.AffectedPeople.Select(person => person.Id.Value)
        }),
        HasAiSuggestionLink = decision.AiSuggestionId is not null
    };

    private static object DescribeEvent(CalendarEvent calendarEvent) => new
    {
        calendarEvent.Title,
        PrimaryCalendar = calendarEvent.PrimaryCalendar.ToString(),
        Category = calendarEvent.Category.ToString(),
        BusyStatus = calendarEvent.BusyStatus.ToString(),
        ReviewStatus = calendarEvent.ReviewStatus.ToString(),
        Time = new
        {
            Date = calendarEvent.Time.Date.ToString("O"),
            StartTime = calendarEvent.Time.StartTime?.ToString("HH:mm:ss"),
            EndTime = calendarEvent.Time.EndTime?.ToString("HH:mm:ss"),
            calendarEvent.Time.IsAllDay
        },
        Participants = calendarEvent.Participants.Select(participant => new
        {
            PersonId = participant.Person.Id.Value,
            Role = participant.Role.ToString(),
            BusyStatus = participant.BusyStatus.ToString()
        }),
        Recurrence = calendarEvent.Recurrence?.Frequency.ToString()
    };

    private static bool ContainsValue(IReadOnlyDictionary<string, string>? metadata, string value) =>
        metadata?.Values.Any(candidate => string.Equals(candidate, value, StringComparison.Ordinal)) == true;

    private sealed class RecordingCalDavStore : IHearthCalendarStore
    {
        public List<EventIntent> Intents { get; } = [];

        public List<AuditEntry> Audits { get; } = [];

        public List<ReviewDecision> Decisions { get; } = [];

        public Dictionary<string, RecordingCalDavObject> Objects { get; } = new(StringComparer.Ordinal);

        public List<CalendarEvent> ApprovedEvents { get; } = [];

        public List<ApprovedEventQuery> Queries { get; } = [];

        public Task StoreIntentAsync(EventIntent intent, CancellationToken cancellationToken)
        {
            Intents.Add(intent);

            return Task.CompletedTask;
        }

        public async Task<CalDavObjectUpsertResult> UpsertCalDavObjectAsync(
            CalDavObjectUpsert upsert,
            CancellationToken cancellationToken)
        {
            var id = CalDavObjectDocumentId.Create(upsert.CalendarId, upsert.ItemId);
            Objects.TryGetValue(id, out var current);
            if (!PreconditionsAllowWrite(upsert, current))
            {
                return new CalDavObjectUpsertResult(
                    CalDavObjectUpsertStatus.PreconditionFailed,
                    current?.IntentId,
                    current?.ETag);
            }

            if (current is not null &&
                string.Equals(current.ContentHash, upsert.ContentHash, StringComparison.Ordinal))
            {
                return new CalDavObjectUpsertResult(
                    CalDavObjectUpsertStatus.Unchanged,
                    current.IntentId,
                    current.ETag);
            }

            var reviewOutcome = await upsert.ReviewOutcomeFactory(cancellationToken);
            if (current is not null)
            {
                RejectPreviousCalDavReview(current.IntentId, upsert.ObservedAt);
            }

            Intents.Add(upsert.Intent);
            Audits.Add(upsert.AuditEntry);
            Decisions.Add(reviewOutcome.Decision);
            Audits.Add(reviewOutcome.AuditEntry);
            if (reviewOutcome.Decision.Event?.ReviewStatus == ReviewStatus.Approved)
            {
                ApprovedEvents.Add(reviewOutcome.Decision.Event);
            }

            Objects[id] = new RecordingCalDavObject(
                id,
                upsert.CalendarId,
                upsert.ItemId,
                upsert.Intent.Id,
                upsert.ContentHash,
                upsert.ETag,
                current?.CreatedAt ?? upsert.ObservedAt,
                upsert.ObservedAt);

            return new CalDavObjectUpsertResult(
                current is null ? CalDavObjectUpsertStatus.Created : CalDavObjectUpsertStatus.Replaced,
                upsert.Intent.Id,
                upsert.ETag,
                reviewOutcome.Decision);
        }

        private void RejectPreviousCalDavReview(
            EventIntentId intentId,
            DateTimeOffset observedAt)
        {
            var decisionIndex = Decisions.FindIndex(decision =>
                decision.IntentId == intentId && decision.Status != ReviewStatus.Rejected);
            if (decisionIndex < 0)
            {
                return;
            }

            var decision = Decisions[decisionIndex];
            var rejectedEvent = decision.Event is null
                ? null
                : decision.Event with { ReviewStatus = ReviewStatus.Rejected };
            if (decision.Event is not null)
            {
                ApprovedEvents.RemoveAll(calendarEvent => calendarEvent.Id == decision.Event.Id);
            }

            Decisions[decisionIndex] = decision with
            {
                Status = ReviewStatus.Rejected,
                Event = rejectedEvent,
                DecidedAt = observedAt,
                DecidedBy = ActorRef.System
            };
            Audits.Add(new AuditEntry(
                AuditEntryId.New(),
                AuditAction.EventRejected,
                ActorRef.System,
                observedAt,
                "CalDAV Smart Inbox object replaced previous review decision.",
                decision.IntentId,
                rejectedEvent?.Id,
                decision.Id,
                new Dictionary<string, string>
                {
                    ["source"] = CalendarSource.CalDav.ToString(),
                    ["reason"] = "CalDavObjectReplaced"
                }));
        }

        private static bool PreconditionsAllowWrite(
            CalDavObjectUpsert upsert,
            RecordingCalDavObject? current)
        {
            if (upsert.IfNoneMatchAny && current is not null)
            {
                return false;
            }

            if (current is not null && upsert.IfNoneMatchETags.Contains(current.ETag, StringComparer.Ordinal))
            {
                return false;
            }

            if (upsert.IfMatchAny && current is null)
            {
                return false;
            }

            if (upsert.IfMatchAny)
            {
                return true;
            }

            if (upsert.IfMatchETags.Count == 0)
            {
                return true;
            }

            return current is not null && upsert.IfMatchETags.Contains(current.ETag, StringComparer.Ordinal);
        }

        public Task StoreIntentWithAuditAsync(
            EventIntent intent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken)
        {
            Intents.Add(intent);
            Audits.Add(auditEntry);

            return Task.CompletedTask;
        }

        public Task<EventIntent?> LoadIntentAsync(EventIntentId id, CancellationToken cancellationToken) =>
            Task.FromResult(Intents.SingleOrDefault(intent => intent.Id == id));

        public Task StoreReviewOutcomeAsync(
            EventIntent intent,
            ReviewOutcome outcome,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreAuditEntryAsync(AuditEntry auditEntry, CancellationToken cancellationToken)
        {
            Audits.Add(auditEntry);

            return Task.CompletedTask;
        }

        public Task<ReviewOutcome?> LoadReviewOutcomeAsync(
            ReviewDecisionId id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReviewDecision?> LoadReviewDecisionAsync(
            ReviewDecisionId id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreReviewDecisionAsync(
            ReviewDecision decision,
            AuditEntry auditEntry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreEditedReviewOutcomeAsync(
            ReviewDecision originalDecision,
            EventIntent revisedIntent,
            ReviewOutcome revisedOutcome,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteApprovedEventAsync(
            CalendarEvent calendarEvent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RescheduleApprovedEventAsync(
            CalendarEvent originalEvent,
            CalendarEvent rescheduledEvent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> QueryApprovedEventsAsync(
            DateOnly from,
            DateOnly to,
            VirtualCalendar calendar,
            CancellationToken cancellationToken)
        {
            Queries.Add(new ApprovedEventQuery(from, to, calendar));

            return Task.FromResult<IReadOnlyList<CalendarEvent>>(VirtualCalendarViews
                .ForCalendar(calendar, ApprovedEvents)
                .Where(calendarEvent => calendarEvent.Time.Date >= from && calendarEvent.Time.Date <= to)
                .ToArray());
        }

        public Task<CalendarEvent?> LoadApprovedEventAsync(
            CalendarEventId id,
            VirtualCalendar calendar,
            CancellationToken cancellationToken)
        {
            var calendarEvent = ApprovedEvents.SingleOrDefault(candidate => candidate.Id == id);
            if (calendarEvent is null)
            {
                return Task.FromResult<CalendarEvent?>(null);
            }

            return Task.FromResult(VirtualCalendarViews.ForCalendar(calendar, [calendarEvent]).SingleOrDefault());
        }

        public Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>(Audits);
    }

    private static object DescribeObject(RecordingCalDavObject calDavObject) => new
    {
        calDavObject.Id,
        calDavObject.CalendarId,
        calDavObject.ItemId,
        HasIntentLink = calDavObject.IntentId.Value != Guid.Empty,
        HasContentHash = !string.IsNullOrWhiteSpace(calDavObject.ContentHash),
        calDavObject.ETag,
        HasCreatedAt = calDavObject.CreatedAt != default,
        HasUpdatedAt = calDavObject.UpdatedAt != default,
        ContainsRawCalDavPassword = calDavObject.ContentHash.Contains(CalDavPassword, StringComparison.Ordinal),
        ContainsRawWriteToken = calDavObject.ContentHash.Contains(WriteToken, StringComparison.Ordinal),
        ContainsRawFeedToken = calDavObject.ContentHash.Contains(FeedToken, StringComparison.Ordinal)
    };

    private sealed record RecordingCalDavObject(
        string Id,
        string CalendarId,
        string ItemId,
        EventIntentId IntentId,
        string ContentHash,
        string ETag,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ApprovedEventQuery(DateOnly From, DateOnly To, VirtualCalendar Calendar);

    private sealed class RecordingNotifier(RecordingCalDavStore store) : ICalendarUpdateNotifier
    {
        public List<CalendarUpdateNotification> Published { get; } = [];

        public bool StoreHadPersistedDecisionWhenPublished { get; private set; }

        public Task PublishAsync(
            IReadOnlyList<CalendarUpdateNotification> notifications,
            CancellationToken cancellationToken)
        {
            StoreHadPersistedDecisionWhenPublished = store.Decisions.Count > 0;
            Published.AddRange(notifications);

            return Task.CompletedTask;
        }
    }

    private sealed class CountingAiReviewProvider(AiReviewSuggestion? suggestion) : IAiReviewProvider
    {
        public int Calls { get; private set; }

        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;

            return ValueTask.FromResult(suggestion);
        }
    }
}
