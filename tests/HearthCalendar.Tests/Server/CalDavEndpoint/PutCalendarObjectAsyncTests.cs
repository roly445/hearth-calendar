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

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponseWithStableETag(response),
            Decisions = store.Decisions.Select(DescribeDecision),
            ApprovedEventCount = store.ApprovedEvents.Count,
            Audits = store.Audits.Select(DescribeAudit),
            Notifications = notifier.Published.Select(notification => notification.Type)
        });
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

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponseWithStableETag(response),
            provider.Calls,
            Decisions = store.Decisions.Select(DescribeDecision),
            ApprovedEvents = store.ApprovedEvents.Select(DescribeEvent)
        });
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

        await Verifier.Verify(new
        {
            First = EndpointSnapshot.ForResponseWithStableETag(first),
            Changed = EndpointSnapshot.ForResponseWithStableETag(changed),
            ETagChanged = first.Headers.ETag?.ToString() != changed.Headers.ETag?.ToString(),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count,
            Decisions = store.Decisions.Select(DescribeDecision),
            ApprovedEvents = store.ApprovedEvents.Select(DescribeEvent),
            Objects = store.Objects.Values.Select(DescribeObject),
            ObjectPointsAtLatestIntent = store.Objects.Values.Single().IntentId == store.Intents[1].Id,
            ObjectETagMatchesResponse = store.Objects.Values.Single().ETag == changed.Headers.ETag?.ToString()
        });
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

        await Verifier.Verify(new
        {
            First = EndpointSnapshot.ForResponseWithStableETag(first),
            Retry = EndpointSnapshot.ForResponseWithStableETag(retry),
            provider.Calls,
            DecisionCount = store.Decisions.Count
        });
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

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count,
            ObjectCount = store.Objects.Count
        });
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

        await Verifier.Verify(new
        {
            First = EndpointSnapshot.ForResponseWithStableETag(first),
            Response = EndpointSnapshot.ForResponseWithStableETag(response),
            ETagMatchesExisting = first.Headers.ETag?.ToString() == response.Headers.ETag?.ToString(),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count,
            ObjectCount = store.Objects.Count
        });
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

        await Verifier.Verify(new
        {
            First = EndpointSnapshot.ForResponseWithStableETag(first),
            Response = EndpointSnapshot.ForResponseWithStableETag(response),
            ETagMatchesExisting = first.Headers.ETag?.ToString() == response.Headers.ETag?.ToString(),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count,
            ObjectCount = store.Objects.Count
        });
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

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        });
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

        await Verifier.Verify(new
        {
            First = EndpointSnapshot.ForResponseWithStableETag(first),
            Retry = EndpointSnapshot.ForResponseWithStableETag(retry),
            ETagReused = first.Headers.ETag?.ToString() == retry.Headers.ETag?.ToString(),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count,
            DecisionCount = store.Decisions.Count,
            ApprovedEventCount = store.ApprovedEvents.Count,
            ObjectCount = store.Objects.Count,
            NotificationCount = notifier.Published.Count
        });
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

        await Verifier.Verify(new
        {
            Upper = EndpointSnapshot.ForResponseWithStableETag(upper),
            Lower = EndpointSnapshot.ForResponseWithStableETag(lower),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count,
            DecisionCount = store.Decisions.Count,
            Objects = store.Objects.Keys.Order().ToArray()
        });
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

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponseWithStableETag(response),
            notifier.StoreHadPersistedDecisionWhenPublished,
            Notifications = notifier.Published.Select(notification => notification.Type)
        });
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

        await Verifier.Verify(new
        {
            ExpectedStatusCode = expectedStatusCode,
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        }).UseParameters(password ?? "missing", expectedStatusCode);
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

        await Verifier.Verify(new
        {
            First = EndpointSnapshot.ForResponseWithStableETag(first),
            Response = EndpointSnapshot.ForResponseWithStableETag(response),
            ETagMatchesExisting = first.Headers.ETag?.ToString() == response.Headers.ETag?.ToString(),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count,
            ObjectCount = store.Objects.Count
        });
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

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        });
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

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            StoredIntentCount = store.Intents.Count,
            AuditCount = store.Audits.Count
        });
    }
}
