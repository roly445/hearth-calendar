using System.Net;
using System.Net.Http.Headers;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HearthCalendar.Tests.Server;

public abstract class FeedEndpointTestBase
{
    protected const string AdultAToken = "test-adult-a-feed-token";
    protected const string CombinedToken = "test-combined-feed-token";
    protected static readonly DateOnly Today = new(2026, 7, 30);

    protected static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory(
        RecordingFeedStore store) =>
        new WebApplicationFactory<HearthCalendar.Server.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = "Host=localhost;Database=hearth_calendar_test",
                        ["Database:SchemaName"] = "hearth_calendar_test",
                        ["Auth:FeedTokens:0:Name"] = "adult-a-feed",
                        ["Auth:FeedTokens:0:TokenHash"] = HearthCalendarSecretHasher.Hash(AdultAToken),
                        ["Auth:FeedTokens:0:AllowedCalendars:0"] = VirtualCalendar.AdultA.ToString(),
                        ["Auth:FeedTokens:0:Scopes:0"] = HearthCalendarAuth.FeedReadScope,
                        ["Auth:FeedTokens:1:Name"] = "combined-feed",
                        ["Auth:FeedTokens:1:TokenHash"] = HearthCalendarSecretHasher.Hash(CombinedToken),
                        ["Auth:FeedTokens:1:AllowedCalendars:0"] = VirtualCalendar.Combined.ToString(),
                        ["Auth:FeedTokens:1:Scopes:0"] = HearthCalendarAuth.FeedReadScope
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHearthCalendarStore>();
                    services.RemoveAll<IHearthCalendarCredentialStore>();
                    services.AddSingleton<IHearthCalendarStore>(store);
                    services.AddSingleton<IHearthCalendarCredentialStore, NoOpCredentialStore>();
                });
            });

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

    protected static CalendarEvent FamilyAllDayEvent(string title, DateOnly date) =>
        CalendarEvent.Approved(
            CalendarEventId.New(),
            title,
            new EventTime(date, null, null, true),
            VirtualCalendar.Family,
            EventCategory.Family,
            BusyStatus.Busy,
            KnownPeople.All.Select(person => new Participant(person, ParticipationRole.Attendee, BusyStatus.Busy)).ToArray(),
            CalendarSource.Test);

    protected static IReadOnlyDictionary<string, string> NormalizeEvent(IReadOnlyDictionary<string, string> properties)
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

    protected sealed class RecordingFeedStore : IHearthCalendarStore
    {
        public List<CalendarEvent> ApprovedEvents { get; } = [];

        public List<ApprovedEventQuery> Queries { get; } = [];

        public Task StoreIntentAsync(EventIntent intent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreIntentWithAuditAsync(
            EventIntent intent,
            AuditEntry auditEntry,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CalDavObjectUpsertResult> UpsertCalDavObjectAsync(
            CalDavObjectUpsert upsert,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EventIntent?> LoadIntentAsync(EventIntentId id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreReviewOutcomeAsync(
            EventIntent intent,
            ReviewOutcome outcome,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreAuditEntryAsync(AuditEntry auditEntry, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    protected sealed record ApprovedEventQuery(DateOnly From, DateOnly To, VirtualCalendar Calendar);
}
public static class IcsAssertions
{
    public static ParsedCalendar Parse(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("END:VCALENDAR", lines[^1]);

        var calendarProperties = new Dictionary<string, string>();
        var events = new List<ParsedEvent>();
        Dictionary<string, string>? currentEvent = null;

        foreach (var line in lines.Skip(1).SkipLast(1))
        {
            if (line == "BEGIN:VEVENT")
            {
                currentEvent = new Dictionary<string, string>();
                continue;
            }

            if (line == "END:VEVENT")
            {
                events.Add(new ParsedEvent(currentEvent ?? throw new InvalidOperationException("Missing VEVENT.")));
                currentEvent = null;
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var name = line[..separator];
            var value = line[(separator + 1)..];
            var properties = currentEvent ?? calendarProperties;
            properties[name] = value;
        }

        return new ParsedCalendar(calendarProperties, events);
    }
}

public sealed record ParsedCalendar(
    IReadOnlyDictionary<string, string> CalendarProperties,
    IReadOnlyList<ParsedEvent> Events);

public sealed record ParsedEvent(IReadOnlyDictionary<string, string> Properties);
