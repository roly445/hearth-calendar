using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.Domain;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

public abstract class MartenPersistenceTestBase(MartenPostgreSqlFixture fixture)
{
    protected static readonly DateOnly Today = new(2026, 7, 30);

    protected ServiceProvider CreateServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = fixture.ConnectionString,
                ["Database:SchemaName"] = $"hearth_calendar_{Guid.NewGuid():N}"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddHearthCalendarPersistence(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    protected static DeterministicEventReviewPipeline Pipeline(IAiReviewProvider provider) =>
        new(Today, aiReviewProvider: provider);

    protected static EventIntent Intent(string text, EventIntentPayload? payload = null) =>
        new(
            EventIntentId.New(),
            CalendarSource.Test,
            ReviewSourceMode.Passive,
            text,
            payload,
            SubmittedAt(),
            ActorRef.System);

    protected static EventIntent CalDavIntent(string text, int minutesOffset = 0) =>
        new(
            EventIntentId.New(),
            CalendarSource.CalDav,
            ReviewSourceMode.Passive,
            text,
            new EventIntentPayload(Today, new TimeOnly(10, 0), new TimeOnly(11, 0)),
            SubmittedAt().AddMinutes(minutesOffset),
            new ActorRef("caldav-app"));

    protected static CalDavObjectUpsert CalDavUpsert(
        string itemId,
        string contentHash,
        string eTag,
        EventIntent intent) =>
        new(
            "smart-inbox",
            itemId,
            contentHash,
            eTag,
            intent,
            new AuditEntry(
                AuditEntryId.New(),
                AuditAction.IntakeIntentSubmitted,
                intent.SubmittedBy,
                intent.SubmittedAt,
                "CalDAV Smart Inbox intent submitted.",
                intent.Id,
                Metadata: new Dictionary<string, string>
                {
                    ["source"] = CalendarSource.CalDav.ToString(),
                    ["mode"] = ReviewSourceMode.Passive.ToString(),
                    ["tokenKind"] = "caldav",
                    ["calendar"] = "smart-inbox",
                    ["itemId"] = itemId,
                    ["etag"] = eTag
                }),
            _ => ValueTask.FromResult(Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAudit(intent)),
            intent.SubmittedAt,
            [],
            IfMatchAny: false,
            [],
            IfNoneMatchAny: false);

    protected static DateTimeOffset SubmittedAt() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    protected static AiReviewSuggestion Suggestion() =>
        new(
            AiReviewSuggestionId.New(),
            "stub",
            "stub-model",
            "Dentist for Adult A",
            VirtualCalendar.AdultA,
            [KnownPeople.AdultA.Id],
            null,
            null,
            0.95m,
            ["Matched placeholder people and a simple calendar type."],
            new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 1)), TimeSpan.Zero));

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

    protected static object? DescribeIntent(EventIntent? intent)
    {
        if (intent is null)
        {
            return null;
        }

        return new
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
            SubmittedAt = intent.SubmittedAt.ToString("O"),
            SubmittedBy = intent.SubmittedBy.Id
        };
    }

    protected static object DescribeCalDavUpsert(CalDavObjectUpsertResult result) => new
    {
        Status = result.Status.ToString(),
        HasIntentLink = result.IntentId is not null && result.IntentId.Value.Value != Guid.Empty,
        result.ETag
    };

    protected static object DescribeCalDavObject(CalDavObjectDocument document) => new
    {
        document.Id,
        document.CalendarId,
        document.ItemId,
        HasIntentLink = document.IntentId != Guid.Empty,
        HasContentHash = !string.IsNullOrWhiteSpace(document.ContentHash),
        document.ETag,
        CreatedAt = document.CreatedAt.ToString("O"),
        UpdatedAt = document.UpdatedAt.ToString("O")
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
        Recurrence = calendarEvent.Recurrence?.Frequency.ToString(),
        Participants = calendarEvent.Participants.Select(participant => new
        {
            PersonId = participant.Person.Id.Value,
            Role = participant.Role.ToString(),
            BusyStatus = participant.BusyStatus.ToString()
        })
    };

    protected static object DescribeDecision(ReviewDecision decision) => new
    {
        Status = decision.Status.ToString(),
        Mode = decision.Mode.ToString(),
        HasAiSuggestionLink = decision.AiSuggestionId is not null,
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
        DecidedAt = decision.DecidedAt.ToString("O"),
        DecidedBy = decision.DecidedBy.Id
    };

    protected static object? DescribeOutcome(ReviewOutcome? outcome)
    {
        if (outcome is null)
        {
            return null;
        }

        return new
        {
            Decision = DescribeDecision(outcome.Decision),
            Audit = DescribeAudit(outcome.AuditEntry),
            AiSuggestion = outcome.AiSuggestion is null
                ? null
                : new
                {
                    outcome.AiSuggestion.Provider,
                    outcome.AiSuggestion.Model,
                    outcome.AiSuggestion.SuggestedTitle,
                    SuggestedCalendar = outcome.AiSuggestion.SuggestedCalendar?.ToString(),
                    SuggestedParticipants = outcome.AiSuggestion.SuggestedParticipants.Select(personId => personId.Value),
                    SuggestedResponsibleAdult = outcome.AiSuggestion.SuggestedResponsibleAdult?.Value,
                    Recurrence = outcome.AiSuggestion.SuggestedRecurrence?.Frequency.ToString(),
                    outcome.AiSuggestion.Confidence,
                    outcome.AiSuggestion.Reasons,
                    CreatedAt = outcome.AiSuggestion.CreatedAt.ToString("O")
                }
        };
    }

    protected static object DescribeAudit(AuditEntry audit) => new
    {
        Action = audit.Action.ToString(),
        Actor = audit.Actor.Id,
        OccurredAt = audit.OccurredAt.ToString("O"),
        audit.Summary,
        HasIntentLink = audit.IntentId is not null,
        HasCalendarEventLink = audit.CalendarEventId is not null,
        HasReviewDecisionLink = audit.ReviewDecisionId is not null,
        audit.Metadata
    };

    protected sealed class StubAiReviewProvider(AiReviewSuggestion suggestion) : IAiReviewProvider
    {
        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AiReviewSuggestion?>(suggestion);
    }
}
[CollectionDefinition(Name)]
public sealed class MartenPostgreSqlCollection : ICollectionFixture<MartenPostgreSqlFixture>
{
    public const string Name = "Marten PostgreSQL";
}

public sealed class MartenPostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgreSql = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string ConnectionString => postgreSql.GetConnectionString();

    public Task InitializeAsync() => postgreSql.StartAsync();

    public async Task DisposeAsync()
    {
        await postgreSql.DisposeAsync();
    }
}
