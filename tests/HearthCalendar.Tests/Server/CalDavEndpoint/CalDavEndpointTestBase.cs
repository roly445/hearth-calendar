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
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HearthCalendar.Tests.Server;

public abstract class CalDavEndpointTestBase
{
    protected const string CalDavUser = "caldav-app";
    protected const string CalDavPassword = "test-caldav-app-password";
    protected const string CalDavReadUser = "caldav-read-app";
    protected const string CalDavReadPassword = "test-caldav-read-app-password";
    protected const string WriteToken = "test-write-token";
    protected const string FeedToken = "test-feed-token";

    protected static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory(
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

    protected static AuthenticationHeaderValue Basic(string user, string password)
    {
        var bytes = Encoding.UTF8.GetBytes($"{user}:{password}");

        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }

    protected static StringContent IcsContent(string content) =>
        new(content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal), Encoding.UTF8, "text/calendar");

    protected static HttpRequestMessage PropFind(string uri, string depth = "0")
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), uri);
        request.Headers.TryAddWithoutValidation("Depth", depth);

        return request;
    }

    protected static HttpRequestMessage Report(string uri, string body)
    {
        var request = new HttpRequestMessage(new HttpMethod("REPORT"), uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml")
        };
        request.Headers.TryAddWithoutValidation("Depth", "1");

        return request;
    }

    protected static IReadOnlyList<CalDavDiscoveryResponse> NormalizeDiscoveryXml(XDocument document)
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

    protected static IReadOnlyList<CalDavReportResponse> NormalizeReportXml(XDocument document)
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

    protected static string NormalizeCalDavHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var lastSlash = href.LastIndexOf("/", StringComparison.Ordinal);

        return lastSlash < 0 ? href : href[..(lastSlash + 1)] + "stable-event.ics";
    }

    protected static IReadOnlyDictionary<string, string> NormalizeIcsEvent(IReadOnlyDictionary<string, string> properties)
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

    protected sealed record CalDavDiscoveryResponse(
        string Href,
        string Status,
        string DisplayName,
        string PrincipalHref,
        string CalendarHomeSet,
        IReadOnlyList<string> ResourceTypes,
        IReadOnlyList<string> Components,
        IReadOnlyList<string> Privileges);

    protected sealed record CalDavReportResponse(
        string Href,
        string ETag,
        IReadOnlyDictionary<string, string> Event);

    protected static string BasicIcs() =>
        """
        BEGIN:VCALENDAR
        BEGIN:VEVENT
        SUMMARY:Family planning
        DTSTART:20260901T100000Z
        DTEND:20260901T110000Z
        END:VEVENT
        END:VCALENDAR
        """;

    protected static CalendarEvent AdultAEvent(
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

    protected static CalendarEvent BirthdayEvent(string title, DateOnly date) =>
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

    protected static object DescribeIntent(EventIntent intent) => new
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

    protected static object DescribeAudit(AuditEntry audit) => new
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

    protected static object DescribeDecision(ReviewDecision decision) => new
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

    protected static object DescribeEvent(CalendarEvent calendarEvent) => new
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

    protected static bool ContainsValue(IReadOnlyDictionary<string, string>? metadata, string value) =>
        metadata?.Values.Any(candidate => string.Equals(candidate, value, StringComparison.Ordinal)) == true;

    protected sealed class RecordingCalDavStore : IHearthCalendarStore
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

    protected static object DescribeObject(RecordingCalDavObject calDavObject) => new
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

    protected sealed record RecordingCalDavObject(
        string Id,
        string CalendarId,
        string ItemId,
        EventIntentId IntentId,
        string ContentHash,
        string ETag,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    protected sealed record ApprovedEventQuery(DateOnly From, DateOnly To, VirtualCalendar Calendar);

    protected sealed class RecordingNotifier(RecordingCalDavStore store) : ICalendarUpdateNotifier
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

    protected sealed class CountingAiReviewProvider(AiReviewSuggestion? suggestion) : IAiReviewProvider
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
