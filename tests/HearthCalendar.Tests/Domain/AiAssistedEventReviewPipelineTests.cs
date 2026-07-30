using HearthCalendar.Shared.Domain;

namespace HearthCalendar.Tests.Domain;

public sealed class AiAssistedEventReviewPipelineTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    [Fact]
    public async Task No_op_provider_leaves_deterministic_behavior_unchanged()
    {
        var intent = Intent("Dentist for Adult A");
        var outcome = await Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAuditAsync(intent);

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Deterministic_approval_does_not_call_ai_provider()
    {
        var outcome = await Pipeline(new ThrowingAiReviewProvider()).ReviewWithAuditAsync(Intent("Family BBQ"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Low_confidence_suggestion_is_recorded_but_not_applied_automatically()
    {
        var suggestion = Suggestion(
            title: "Dentist for Adult A",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.4m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_resolves_allowed_ambiguity_when_safety_passes()
    {
        var suggestion = Suggestion(
            title: "Dentist for Adult A",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.92m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_with_unknown_participant_is_not_applied()
    {
        var suggestion = Suggestion(
            title: "Dentist",
            calendar: VirtualCalendar.AdultA,
            participants: [new PersonId("unknown-person")],
            confidence: 0.95m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_with_calendar_participant_mismatch_is_not_applied()
    {
        var suggestion = Suggestion(
            title: "Dentist",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultB.Id],
            confidence: 0.95m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task High_confidence_suggestion_for_projection_calendar_is_not_applied()
    {
        var suggestion = Suggestion(
            title: "Dentist",
            calendar: VirtualCalendar.Combined,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.95m);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Responsible_adult_suggestion_must_reference_an_adult()
    {
        var suggestion = Suggestion(
            title: "Child swimming",
            calendar: VirtualCalendar.Child,
            participants: [KnownPeople.Child.Id, KnownPeople.AdultB.Id],
            confidence: 0.95m,
            responsibleAdult: KnownPeople.Child.Id);
        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("swimming"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Deterministic_clash_staging_wins_over_ai_approval_suggestion()
    {
        var existing = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Family BBQ",
            new EventTime(Today, new TimeOnly(12, 0), new TimeOnly(14, 0), false),
            VirtualCalendar.Family,
            EventCategory.Family,
            BusyStatus.Busy,
            KnownPeople.All.Select(person => new Participant(person, ParticipationRole.Attendee, BusyStatus.Busy)).ToArray(),
            CalendarSource.Test);
        var suggestion = Suggestion(
            title: "Dentist for Adult A",
            calendar: VirtualCalendar.AdultA,
            participants: [KnownPeople.AdultA.Id],
            confidence: 0.98m);
        var intent = Intent(
            "dentist",
            new EventIntentPayload(Today, new TimeOnly(13, 0), new TimeOnly(13, 30)));

        var outcome = await Pipeline(new StubAiReviewProvider(suggestion), existing).ReviewWithAuditAsync(intent);

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Deterministic_recurrence_safety_wins_over_ai_suggestion()
    {
        var suggestion = Suggestion(
            title: "Adult B birthday",
            calendar: VirtualCalendar.Events,
            participants: [KnownPeople.AdultB.Id],
            confidence: 0.98m,
            recurrence: new RecurrenceRule(RecurrenceFrequency.Yearly));

        var outcome = await Pipeline(new StubAiReviewProvider(suggestion)).ReviewWithAuditAsync(Intent("Adult B birthday"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Provider_failure_degrades_to_deterministic_review()
    {
        var outcome = await Pipeline(new ThrowingAiReviewProvider()).ReviewWithAuditAsync(Intent("dentist"));

        await Verifier.Verify(DescribeOutcome(outcome));
    }

    [Fact]
    public async Task Provider_payload_uses_public_minimal_context_only()
    {
        var provider = new CapturingAiReviewProvider();
        var intent = Intent(
            "dentist",
            new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));

        await Pipeline(provider).ReviewWithAuditAsync(intent);

        await Verifier.Verify(new
        {
            Request = DescribeRequest(provider.Request ?? throw new InvalidOperationException("Expected captured request."))
        });
    }

    private static DeterministicEventReviewPipeline Pipeline(
        IAiReviewProvider provider,
        params CalendarEvent[] existingEvents) =>
        new(Today, existingEvents, provider);

    private static EventIntent Intent(
        string text,
        EventIntentPayload? payload = null,
        ReviewSourceMode sourceMode = ReviewSourceMode.Passive) =>
        new(
            EventIntentId.New(),
            CalendarSource.Test,
            sourceMode,
            text,
            payload,
            new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            ActorRef.System);

    private static AiReviewSuggestion Suggestion(
        string title,
        VirtualCalendar calendar,
        IReadOnlyList<PersonId> participants,
        decimal confidence,
        PersonId? responsibleAdult = null,
        RecurrenceRule? recurrence = null) =>
        new(
            AiReviewSuggestionId.New(),
            "stub",
            "stub-model",
            title,
            calendar,
            participants,
            responsibleAdult,
            recurrence,
            confidence,
            ["Matched placeholder people and a simple calendar type."],
            new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 1)), TimeSpan.Zero));

    private static object DescribeOutcome(ReviewOutcome outcome) => new
    {
        Decision = DescribeDecision(outcome.Decision),
        AiSuggestion = DescribeSuggestion(outcome.AiSuggestion),
        AuditEntry = new
        {
            Action = outcome.AuditEntry.Action.ToString(),
            Actor = outcome.AuditEntry.Actor.Id,
            OccurredAt = outcome.AuditEntry.OccurredAt.ToString("O"),
            outcome.AuditEntry.Summary,
            HasIntentLink = outcome.AuditEntry.IntentId is not null,
            HasCalendarEventLink = outcome.AuditEntry.CalendarEventId is not null,
            HasReviewDecisionLink = outcome.AuditEntry.ReviewDecisionId is not null,
            outcome.AuditEntry.Metadata
        }
    };

    private static object DescribeDecision(ReviewDecision decision) => new
    {
        Status = decision.Status.ToString(),
        Mode = decision.Mode.ToString(),
        HasAiSuggestionLink = decision.AiSuggestionId is not null,
        Event = DescribeEvent(decision.Event),
        Reasons = decision.Reasons.Select(reason => new
        {
            Code = reason.Code.ToString(),
            reason.Message
        }).ToArray(),
        Clashes = decision.Clashes.Select(DescribeClash).ToArray(),
        DecidedAt = decision.DecidedAt.ToString("O"),
        DecidedBy = decision.DecidedBy.Id
    };

    private static object? DescribeEvent(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
        {
            return null;
        }

        return new
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
                Person = participant.Person.DisplayName,
                PersonId = participant.Person.Id.Value,
                Role = participant.Role.ToString(),
                BusyStatus = participant.BusyStatus.ToString()
            }).ToArray(),
            ResponsibleAdult = calendarEvent.ResponsibleAdult is null
                ? null
                : new
                {
                    Adult = calendarEvent.ResponsibleAdult.Adult.DisplayName,
                    AdultId = calendarEvent.ResponsibleAdult.Adult.Id.Value,
                    Kind = calendarEvent.ResponsibleAdult.Kind.ToString(),
                    Source = calendarEvent.ResponsibleAdult.Source.ToString()
                },
            HasParentEvent = calendarEvent.ParentEventId is not null
        };
    }

    private static object? DescribeSuggestion(AiReviewSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return null;
        }

        return new
        {
            suggestion.Provider,
            suggestion.Model,
            suggestion.SuggestedTitle,
            SuggestedCalendar = suggestion.SuggestedCalendar?.ToString(),
            SuggestedParticipants = suggestion.SuggestedParticipants.Select(personId => personId.Value).ToArray(),
            SuggestedResponsibleAdult = suggestion.SuggestedResponsibleAdult?.Value,
            Recurrence = suggestion.SuggestedRecurrence?.Frequency.ToString(),
            suggestion.Confidence,
            suggestion.Reasons,
            CreatedAt = suggestion.CreatedAt.ToString("O")
        };
    }

    private static object DescribeClash(Clash clash) => new
    {
        Severity = clash.Severity.ToString(),
        clash.Summary,
        AffectedPeople = clash.AffectedPeople.Select(person => new
        {
            person.DisplayName,
            PersonId = person.Id.Value
        }).ToArray()
    };

    private static object DescribeRequest(AiReviewRequest request) => new
    {
        HasIntentId = request.IntentId.Value != Guid.Empty,
        Source = request.Source.ToString(),
        SourceMode = request.SourceMode.ToString(),
        request.RawText,
        Payload = request.Payload is null
            ? null
            : new
            {
                Date = request.Payload.Date?.ToString("O"),
                StartTime = request.Payload.StartTime?.ToString("HH:mm:ss"),
                EndTime = request.Payload.EndTime?.ToString("HH:mm:ss")
            },
        SubmittedAt = request.SubmittedAt.ToString("O")
    };

    private sealed class StubAiReviewProvider(AiReviewSuggestion? suggestion) : IAiReviewProvider
    {
        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(suggestion);
    }

    private sealed class ThrowingAiReviewProvider : IAiReviewProvider
    {
        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Provider unavailable.");
        }
    }

    private sealed class CapturingAiReviewProvider : IAiReviewProvider
    {
        public AiReviewRequest? Request { get; private set; }

        public ValueTask<AiReviewSuggestion?> ReviewAsync(
            AiReviewRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;

            return ValueTask.FromResult<AiReviewSuggestion?>(null);
        }
    }
}
