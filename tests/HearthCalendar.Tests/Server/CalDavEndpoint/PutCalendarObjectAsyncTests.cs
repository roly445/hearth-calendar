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

public sealed class PutCalendarObjectAsyncTests : CalDavEndpointTestBase
{
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
}
