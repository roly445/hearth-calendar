using HearthCalendar.Server.Persistence;
using HearthCalendar.Shared.Domain;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
public sealed class MartenPersistenceTests(MartenPostgreSqlFixture fixture)
{
    private static readonly DateOnly Today = new(2026, 7, 30);

    [Fact]
    public async Task EventIntentDocument_round_trips_source_payload_actor_and_timestamp()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var intent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));

        await store.StoreIntentAsync(intent, CancellationToken.None);
        var loaded = await store.LoadIntentAsync(intent.Id, CancellationToken.None);

        await Verifier.Verify(DescribeIntent(loaded));
    }

    [Fact]
    public async Task ReviewWorkflow_persists_intent_decision_event_suggestion_and_audit_in_one_marten_commit()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var intent = Intent("dentist");
        var outcome = await Pipeline(new StubAiReviewProvider(Suggestion())).ReviewWithAuditAsync(
            intent,
            CancellationToken.None);

        await store.StoreReviewOutcomeAsync(intent, outcome, CancellationToken.None);
        var approvedEvents = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var audits = await store.QueryAuditEntriesAsync(CancellationToken.None);
        var loadedOutcome = await store.LoadReviewOutcomeAsync(outcome.Decision.Id, CancellationToken.None);

        await Verifier.Verify(new
        {
            ApprovedEvents = approvedEvents.Select(DescribeEvent),
            Audits = audits.Select(DescribeAudit),
            LoadedOutcome = DescribeOutcome(loadedOutcome)
        });
    }

    [Fact]
    public async Task ApprovedEventsQuery_excludes_staged_rejected_and_other_calendar_items()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var approvedAdultA = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Dentist for Adult A",
            new EventTime(Today, new TimeOnly(9, 0), new TimeOnly(9, 30), false),
            VirtualCalendar.AdultA,
            EventCategory.Personal,
            BusyStatus.Busy,
            [new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy)],
            CalendarSource.Test);
        var familyEvent = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Family BBQ",
            new EventTime(Today, null, null, true),
            VirtualCalendar.Family,
            EventCategory.Family,
            BusyStatus.Busy,
            KnownPeople.All.Select(person => new Participant(person, ParticipationRole.Attendee, BusyStatus.Busy)).ToArray(),
            CalendarSource.Test);
        var stagedAdultA = approvedAdultA with
        {
            Id = CalendarEventId.New(),
            Title = "Staged Adult A item",
            ReviewStatus = ReviewStatus.Staged
        };
        var rejectedAdultA = approvedAdultA with
        {
            Id = CalendarEventId.New(),
            Title = "Rejected Adult A item",
            ReviewStatus = ReviewStatus.Rejected
        };
        var eventsReference = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Adult B birthday",
            new EventTime(Today, null, null, true),
            VirtualCalendar.Events,
            EventCategory.Birthday,
            BusyStatus.Free,
            [new Participant(KnownPeople.AdultB, ParticipationRole.Attendee, BusyStatus.Free)],
            CalendarSource.Test,
            new RecurrenceRule(RecurrenceFrequency.Yearly));

        session.Store(approvedAdultA.ToDocument());
        session.Store(familyEvent.ToDocument());
        session.Store(stagedAdultA.ToDocument());
        session.Store(rejectedAdultA.ToDocument());
        session.Store(eventsReference.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);

        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var adultAEvents = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var eventReferenceEvents = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.Events,
            CancellationToken.None);

        await Verifier.Verify(new
        {
            AdultA = adultAEvents.Select(DescribeEvent),
            Events = eventReferenceEvents.Select(DescribeEvent)
        });
    }

    [Fact]
    public async Task ReviewQueueQuery_returns_staged_decisions_only()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var stagedIntent = Intent("dentist");
        var stagedOutcome = Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAudit(stagedIntent);
        var stagedCandidateIntent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(Today.AddDays(-1), new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var stagedCandidateOutcome = Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAudit(stagedCandidateIntent);
        var approvedIntent = Intent("Family BBQ");
        var approvedOutcome = Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAudit(approvedIntent);

        await store.StoreReviewOutcomeAsync(stagedIntent, stagedOutcome, CancellationToken.None);
        await store.StoreReviewOutcomeAsync(stagedCandidateIntent, stagedCandidateOutcome, CancellationToken.None);
        await store.StoreReviewOutcomeAsync(approvedIntent, approvedOutcome, CancellationToken.None);
        var reviewQueue = await store.QueryReviewQueueAsync(CancellationToken.None);

        await Verifier.Verify(reviewQueue.Select(DescribeDecision));
    }

    [Fact]
    public async Task Credential_and_feed_token_documents_store_hashes_without_raw_secrets()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        session.Store(new ClientCredentialDocument
        {
            Id = Guid.NewGuid(),
            ClientName = "home-assistant",
            SecretHash = "sha256:credential-hash-placeholder",
            Scopes = ["intake:write"],
            CreatedAt = SubmittedAt(),
            LastUsedAt = SubmittedAt().AddMinutes(5)
        });
        session.Store(new FeedTokenDocument
        {
            Id = Guid.NewGuid(),
            Name = "adult-a-feed",
            TokenHash = "sha256:feed-hash-placeholder",
            AllowedCalendars = [VirtualCalendar.AdultA.ToString()],
            Scopes = ["feed:adult-a"],
            CreatedAt = SubmittedAt(),
            LastUsedAt = SubmittedAt().AddMinutes(10)
        });
        await session.SaveChangesAsync(CancellationToken.None);

        var credentials = await session.Query<ClientCredentialDocument>().ToListAsync(CancellationToken.None);
        var feedTokens = await session.Query<FeedTokenDocument>().ToListAsync(CancellationToken.None);

        await Verifier.Verify(new
        {
            Credentials = credentials.Select(credential => new
            {
                credential.ClientName,
                HasSecretHash = !string.IsNullOrWhiteSpace(credential.SecretHash),
                credential.Scopes,
                LastUsedAt = credential.LastUsedAt?.ToString("O"),
                credential.RevokedAt
            }),
            FeedTokens = feedTokens.Select(feedToken => new
            {
                feedToken.Name,
                HasTokenHash = !string.IsNullOrWhiteSpace(feedToken.TokenHash),
                feedToken.AllowedCalendars,
                feedToken.Scopes,
                LastUsedAt = feedToken.LastUsedAt?.ToString("O"),
                feedToken.RevokedAt
            })
        });
    }

    private ServiceProvider CreateServices()
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

    private static DeterministicEventReviewPipeline Pipeline(IAiReviewProvider provider) =>
        new(Today, aiReviewProvider: provider);

    private static EventIntent Intent(string text, EventIntentPayload? payload = null) =>
        new(
            EventIntentId.New(),
            CalendarSource.Test,
            ReviewSourceMode.Passive,
            text,
            payload,
            SubmittedAt(),
            ActorRef.System);

    private static DateTimeOffset SubmittedAt() => new(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static AiReviewSuggestion Suggestion() =>
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

    private static object? DescribeIntent(EventIntent? intent)
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
        Recurrence = calendarEvent.Recurrence?.Frequency.ToString(),
        Participants = calendarEvent.Participants.Select(participant => new
        {
            PersonId = participant.Person.Id.Value,
            Role = participant.Role.ToString(),
            BusyStatus = participant.BusyStatus.ToString()
        })
    };

    private static object DescribeDecision(ReviewDecision decision) => new
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

    private static object? DescribeOutcome(ReviewOutcome? outcome)
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

    private static object DescribeAudit(AuditEntry audit) => new
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

    private sealed class StubAiReviewProvider(AiReviewSuggestion suggestion) : IAiReviewProvider
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
