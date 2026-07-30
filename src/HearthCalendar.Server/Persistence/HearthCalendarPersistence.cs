using HearthCalendar.Shared.Domain;
using Marten;

namespace HearthCalendar.Server.Persistence;

public interface IHearthCalendarStore
{
    Task StoreIntentAsync(EventIntent intent, CancellationToken cancellationToken);

    Task StoreIntentWithAuditAsync(
        EventIntent intent,
        AuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<EventIntent?> LoadIntentAsync(EventIntentId id, CancellationToken cancellationToken);

    Task StoreReviewOutcomeAsync(EventIntent intent, ReviewOutcome outcome, CancellationToken cancellationToken);

    Task StoreAuditEntryAsync(AuditEntry auditEntry, CancellationToken cancellationToken);

    Task<ReviewOutcome?> LoadReviewOutcomeAsync(ReviewDecisionId id, CancellationToken cancellationToken);

    Task<ReviewDecision?> LoadReviewDecisionAsync(ReviewDecisionId id, CancellationToken cancellationToken);

    Task StoreReviewDecisionAsync(
        ReviewDecision decision,
        AuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task StoreEditedReviewOutcomeAsync(
        ReviewDecision originalDecision,
        EventIntent revisedIntent,
        ReviewOutcome revisedOutcome,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarEvent>> QueryApprovedEventsAsync(
        DateOnly from,
        DateOnly to,
        VirtualCalendar calendar,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken);
}

public sealed class StaleReviewDecisionException(ReviewDecisionId reviewDecisionId)
    : InvalidOperationException($"Review decision {reviewDecisionId.Value} is no longer staged.")
{
    public ReviewDecisionId ReviewDecisionId { get; } = reviewDecisionId;
}

public sealed class MartenHearthCalendarStore(IDocumentSession session) : IHearthCalendarStore
{
    public async Task StoreIntentAsync(EventIntent intent, CancellationToken cancellationToken)
    {
        session.Store(intent.ToDocument());

        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task StoreIntentWithAuditAsync(
        EventIntent intent,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        session.Store(intent.ToDocument());
        session.Store(auditEntry.ToDocument());

        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<EventIntent?> LoadIntentAsync(EventIntentId id, CancellationToken cancellationToken)
    {
        var document = await session.LoadAsync<EventIntentDocument>(id.Value, cancellationToken);

        return document?.ToDomain();
    }

    public async Task StoreReviewOutcomeAsync(
        EventIntent intent,
        ReviewOutcome outcome,
        CancellationToken cancellationToken)
    {
        session.Store(intent.ToDocument());

        if (outcome.AiSuggestion is not null)
        {
            session.Store(outcome.AiSuggestion.ToDocument(intent.Id));
        }

        if (outcome.Decision.Event is not null)
        {
            session.Store(outcome.Decision.Event.ToDocument());
        }

        session.Store(outcome.Decision.ToDocument());
        session.Store(outcome.AuditEntry.ToDocument());

        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task StoreAuditEntryAsync(AuditEntry auditEntry, CancellationToken cancellationToken)
    {
        session.Store(auditEntry.ToDocument());

        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReviewOutcome?> LoadReviewOutcomeAsync(
        ReviewDecisionId id,
        CancellationToken cancellationToken)
    {
        var decisionDocument = await session.LoadAsync<ReviewDecisionDocument>(id.Value, cancellationToken);
        if (decisionDocument is null)
        {
            return null;
        }

        var eventDocument = decisionDocument.CalendarEventId is null
            ? null
            : await session.LoadAsync<CalendarEventDocument>(decisionDocument.CalendarEventId.Value, cancellationToken);
        var suggestionDocument = decisionDocument.AiSuggestionId is null
            ? null
            : await session.LoadAsync<AiReviewSuggestionDocument>(decisionDocument.AiSuggestionId.Value, cancellationToken);
        var auditDocument = await session.Query<AuditEntryDocument>()
            .Where(audit => audit.ReviewDecisionId == decisionDocument.Id)
            .OrderBy(audit => audit.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (auditDocument is null)
        {
            return null;
        }

        return new ReviewOutcome(
            decisionDocument.ToDomain(eventDocument?.ToDomain()),
            auditDocument.ToDomain(),
            suggestionDocument?.ToDomain());
    }

    public async Task<ReviewDecision?> LoadReviewDecisionAsync(
        ReviewDecisionId id,
        CancellationToken cancellationToken)
    {
        var decisionDocument = await session.LoadAsync<ReviewDecisionDocument>(id.Value, cancellationToken);
        if (decisionDocument is null)
        {
            return null;
        }

        var eventDocument = decisionDocument.CalendarEventId is null
            ? null
            : await session.LoadAsync<CalendarEventDocument>(decisionDocument.CalendarEventId.Value, cancellationToken);

        return decisionDocument.ToDomain(eventDocument?.ToDomain());
    }

    public async Task StoreReviewDecisionAsync(
        ReviewDecision decision,
        AuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var currentDecision = await session.LoadAsync<ReviewDecisionDocument>(
            decision.Id.Value,
            cancellationToken);
        if (currentDecision?.Status != nameof(ReviewStatus.Staged))
        {
            throw new StaleReviewDecisionException(decision.Id);
        }

        if (decision.Event is not null)
        {
            session.Store(decision.Event.ToDocument());
        }

        session.Store(decision.ToDocument());
        session.Store(auditEntry.ToDocument());

        await SaveChangesForReviewDecisionAsync(decision.Id, cancellationToken);
    }

    public async Task StoreEditedReviewOutcomeAsync(
        ReviewDecision originalDecision,
        EventIntent revisedIntent,
        ReviewOutcome revisedOutcome,
        CancellationToken cancellationToken)
    {
        var currentDecision = await session.LoadAsync<ReviewDecisionDocument>(
            originalDecision.Id.Value,
            cancellationToken);
        if (currentDecision?.Status != nameof(ReviewStatus.Staged))
        {
            throw new StaleReviewDecisionException(originalDecision.Id);
        }

        var rejectedOriginal = originalDecision with
        {
            Status = ReviewStatus.Rejected,
            Event = originalDecision.Event is null ? null : originalDecision.Event with { ReviewStatus = ReviewStatus.Rejected },
            DecidedAt = revisedIntent.SubmittedAt,
            DecidedBy = ActorRef.System
        };

        if (rejectedOriginal.Event is not null)
        {
            session.Store(rejectedOriginal.Event.ToDocument());
        }

        session.Store(rejectedOriginal.ToDocument());
        session.Store(revisedIntent.ToDocument());

        if (revisedOutcome.AiSuggestion is not null)
        {
            session.Store(revisedOutcome.AiSuggestion.ToDocument(revisedIntent.Id));
        }

        if (revisedOutcome.Decision.Event is not null)
        {
            session.Store(revisedOutcome.Decision.Event.ToDocument());
        }

        session.Store(revisedOutcome.Decision.ToDocument());
        session.Store(revisedOutcome.AuditEntry.ToDocument());

        await SaveChangesForReviewDecisionAsync(originalDecision.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarEvent>> QueryApprovedEventsAsync(
        DateOnly from,
        DateOnly to,
        VirtualCalendar calendar,
        CancellationToken cancellationToken)
    {
        var documents = await session.Query<CalendarEventDocument>()
            .Where(calendarEvent =>
                calendarEvent.ReviewStatus == nameof(ReviewStatus.Approved) &&
                calendarEvent.Time.Date >= from &&
                calendarEvent.Time.Date <= to)
            .OrderBy(calendarEvent => calendarEvent.Time.Date)
            .ThenBy(calendarEvent => calendarEvent.Title)
            .ToListAsync(cancellationToken);
        var events = documents.Select(document => document.ToDomain()).ToArray();

        return VirtualCalendarViews.ForCalendar(calendar, events);
    }

    public async Task<IReadOnlyList<ReviewDecision>> QueryReviewQueueAsync(CancellationToken cancellationToken)
    {
        var documents = await session.Query<ReviewDecisionDocument>()
            .Where(decision => decision.Status == nameof(ReviewStatus.Staged))
            .OrderBy(decision => decision.DecidedAt)
            .ToListAsync(cancellationToken);
        var eventIds = documents
            .Select(document => document.CalendarEventId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToArray();
        var eventDocuments = eventIds.Length == 0
            ? new Dictionary<Guid, CalendarEventDocument>()
            : (await session.Query<CalendarEventDocument>()
                .Where(calendarEvent => eventIds.Contains(calendarEvent.Id))
                .ToListAsync(cancellationToken))
                .ToDictionary(calendarEvent => calendarEvent.Id);

        return documents
            .Select(document => document.ToDomain(
                document.CalendarEventId is null || !eventDocuments.TryGetValue(document.CalendarEventId.Value, out var eventDocument)
                    ? null
                    : eventDocument.ToDomain()))
            .ToArray();
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAuditEntriesAsync(CancellationToken cancellationToken)
    {
        var documents = await session.Query<AuditEntryDocument>()
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToArray();
    }

    private async Task SaveChangesForReviewDecisionAsync(
        ReviewDecisionId reviewDecisionId,
        CancellationToken cancellationToken)
    {
        try
        {
            await session.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyException(exception))
        {
            throw new StaleReviewDecisionException(reviewDecisionId);
        }
    }

    private static bool IsConcurrencyException(Exception exception) =>
        exception.GetType().Name.Contains("Concurrency", StringComparison.Ordinal) ||
        exception.GetType().FullName?.Contains("Concurrency", StringComparison.Ordinal) == true;

}

public static class HearthCalendarDocumentMapping
{
    public static EventIntentDocument ToDocument(this EventIntent intent) =>
        new()
        {
            Id = intent.Id.Value,
            Source = intent.Source.ToString(),
            SourceMode = intent.SourceMode.ToString(),
            RawText = intent.RawText,
            Payload = intent.Payload is null
                ? null
                : new IntentPayloadDocument(intent.Payload.Date, intent.Payload.StartTime, intent.Payload.EndTime),
            SubmittedAt = intent.SubmittedAt,
            SubmittedBy = intent.SubmittedBy.Id
        };

    public static EventIntent ToDomain(this EventIntentDocument document) =>
        new(
            new EventIntentId(document.Id),
            Enum.Parse<CalendarSource>(document.Source),
            Enum.Parse<ReviewSourceMode>(document.SourceMode),
            document.RawText,
            document.Payload is null
                ? null
                : new EventIntentPayload(document.Payload.Date, document.Payload.StartTime, document.Payload.EndTime),
            document.SubmittedAt,
            new ActorRef(document.SubmittedBy));

    public static CalendarEventDocument ToDocument(this CalendarEvent calendarEvent) =>
        new()
        {
            Id = calendarEvent.Id.Value,
            Title = calendarEvent.Title,
            Time = new EventTimeDocument(
                calendarEvent.Time.Date,
                calendarEvent.Time.StartTime,
                calendarEvent.Time.EndTime,
                calendarEvent.Time.IsAllDay),
            PrimaryCalendar = calendarEvent.PrimaryCalendar.ToString(),
            Category = calendarEvent.Category.ToString(),
            BusyStatus = calendarEvent.BusyStatus.ToString(),
            Participants = calendarEvent.Participants
                .Select(participant => new ParticipantDocument(
                    participant.Person.Id.Value,
                    participant.Person.DisplayName,
                    participant.Person.Kind.ToString(),
                    participant.Role.ToString(),
                    participant.BusyStatus.ToString()))
                .ToArray(),
            Source = calendarEvent.Source.ToString(),
            ReviewStatus = calendarEvent.ReviewStatus.ToString(),
            Recurrence = calendarEvent.Recurrence is null
                ? null
                : new RecurrenceRuleDocument(calendarEvent.Recurrence.Frequency.ToString()),
            ResponsibleAdult = calendarEvent.ResponsibleAdult is null
                ? null
                : new ResponsibleAdultDocument(
                    calendarEvent.ResponsibleAdult.Adult.Id.Value,
                    calendarEvent.ResponsibleAdult.Adult.DisplayName,
                    calendarEvent.ResponsibleAdult.Kind.ToString(),
                    calendarEvent.ResponsibleAdult.Source.ToString()),
            ParentEventId = calendarEvent.ParentEventId?.Value
        };

    public static CalendarEvent ToDomain(this CalendarEventDocument document) =>
        new(
            new CalendarEventId(document.Id),
            document.Title,
            new EventTime(
                document.Time.Date,
                document.Time.StartTime,
                document.Time.EndTime,
                document.Time.IsAllDay),
            Enum.Parse<VirtualCalendar>(document.PrimaryCalendar),
            Enum.Parse<EventCategory>(document.Category),
            Enum.Parse<BusyStatus>(document.BusyStatus),
            document.Participants.Select(participant => new Participant(
                new Person(
                    new PersonId(participant.PersonId),
                    participant.DisplayName,
                    Enum.Parse<PersonKind>(participant.Kind)),
                Enum.Parse<ParticipationRole>(participant.Role),
                Enum.Parse<BusyStatus>(participant.BusyStatus))).ToArray(),
            Enum.Parse<CalendarSource>(document.Source),
            Enum.Parse<ReviewStatus>(document.ReviewStatus),
            document.Recurrence is null
                ? null
                : new RecurrenceRule(Enum.Parse<RecurrenceFrequency>(document.Recurrence.Frequency)),
            document.ResponsibleAdult is null
                ? null
                : new ResponsibleAdult(
                    new Person(
                        new PersonId(document.ResponsibleAdult.AdultPersonId),
                        document.ResponsibleAdult.DisplayName,
                        PersonKind.Adult),
                    Enum.Parse<ResponsibilityKind>(document.ResponsibleAdult.Kind),
                    Enum.Parse<ResponsibilitySource>(document.ResponsibleAdult.Source)),
            document.ParentEventId is null ? null : new CalendarEventId(document.ParentEventId.Value));

    public static ReviewDecisionDocument ToDocument(this ReviewDecision decision) =>
        new()
        {
            Id = decision.Id.Value,
            IntentId = decision.IntentId.Value,
            Status = decision.Status.ToString(),
            Mode = decision.Mode.ToString(),
            Reasons = decision.Reasons
                .Select(reason => new DecisionReasonDocument(reason.Code.ToString(), reason.Message))
                .ToArray(),
            Clashes = decision.Clashes
                .Select(clash => new ClashDocument(
                    clash.ConflictingEventId.Value,
                    clash.AffectedPeople.Select(person => person.Id.Value).ToArray(),
                    clash.Severity.ToString(),
                    clash.Summary))
                .ToArray(),
            CalendarEventId = decision.Event?.Id.Value,
            DecidedAt = decision.DecidedAt,
            DecidedBy = decision.DecidedBy.Id,
            AiSuggestionId = decision.AiSuggestionId?.Value
        };

    public static ReviewDecision ToDomain(this ReviewDecisionDocument document, CalendarEvent? calendarEvent = null) =>
        new(
            new ReviewDecisionId(document.Id),
            new EventIntentId(document.IntentId),
            Enum.Parse<ReviewStatus>(document.Status),
            Enum.Parse<DecisionMode>(document.Mode),
            document.Reasons
                .Select(reason => new DecisionReason(Enum.Parse<DecisionReasonCode>(reason.Code), reason.Message))
                .ToArray(),
            document.Clashes
                .Select(clash => new Clash(
                    new CalendarEventId(clash.ConflictingEventId),
                    clash.AffectedPersonIds
                        .Select(personId => KnownPeople.All.FirstOrDefault(person => person.Id.Value == personId) ??
                            new Person(new PersonId(personId), personId, PersonKind.Adult))
                        .ToArray(),
                    Enum.Parse<ClashSeverity>(clash.Severity),
                    clash.Summary))
                .ToArray(),
            calendarEvent,
            document.DecidedAt,
            new ActorRef(document.DecidedBy),
            document.AiSuggestionId is null ? null : new AiReviewSuggestionId(document.AiSuggestionId.Value));

    public static AiReviewSuggestion ToDomain(this AiReviewSuggestionDocument document) =>
        new(
            new AiReviewSuggestionId(document.Id),
            document.Provider,
            document.Model,
            document.SuggestedTitle,
            string.IsNullOrWhiteSpace(document.SuggestedCalendar)
                ? null
                : Enum.Parse<VirtualCalendar>(document.SuggestedCalendar),
            document.SuggestedParticipants.Select(personId => new PersonId(personId)).ToArray(),
            string.IsNullOrWhiteSpace(document.SuggestedResponsibleAdult)
                ? null
                : new PersonId(document.SuggestedResponsibleAdult),
            document.SuggestedRecurrence is null
                ? null
                : new RecurrenceRule(Enum.Parse<RecurrenceFrequency>(document.SuggestedRecurrence.Frequency)),
            document.Confidence,
            document.Reasons,
            document.CreatedAt);

    public static AiReviewSuggestionDocument ToDocument(this AiReviewSuggestion suggestion, EventIntentId intentId) =>
        new()
        {
            Id = suggestion.Id.Value,
            IntentId = intentId.Value,
            Provider = suggestion.Provider,
            Model = suggestion.Model,
            SuggestedTitle = suggestion.SuggestedTitle,
            SuggestedCalendar = suggestion.SuggestedCalendar?.ToString(),
            SuggestedParticipants = suggestion.SuggestedParticipants.Select(personId => personId.Value).ToArray(),
            SuggestedResponsibleAdult = suggestion.SuggestedResponsibleAdult?.Value,
            SuggestedRecurrence = suggestion.SuggestedRecurrence is null
                ? null
                : new RecurrenceRuleDocument(suggestion.SuggestedRecurrence.Frequency.ToString()),
            Confidence = suggestion.Confidence,
            Reasons = suggestion.Reasons,
            CreatedAt = suggestion.CreatedAt
        };

    public static AuditEntryDocument ToDocument(this AuditEntry auditEntry) =>
        new()
        {
            Id = auditEntry.Id.Value,
            Action = auditEntry.Action.ToString(),
            Actor = auditEntry.Actor.Id,
            OccurredAt = auditEntry.OccurredAt,
            Summary = auditEntry.Summary,
            IntentId = auditEntry.IntentId?.Value,
            CalendarEventId = auditEntry.CalendarEventId?.Value,
            ReviewDecisionId = auditEntry.ReviewDecisionId?.Value,
            Metadata = auditEntry.Metadata ?? new Dictionary<string, string>()
        };

    public static AuditEntry ToDomain(this AuditEntryDocument document) =>
        new(
            new AuditEntryId(document.Id),
            Enum.Parse<AuditAction>(document.Action),
            new ActorRef(document.Actor),
            document.OccurredAt,
            document.Summary,
            document.IntentId is null ? null : new EventIntentId(document.IntentId.Value),
            document.CalendarEventId is null ? null : new CalendarEventId(document.CalendarEventId.Value),
            document.ReviewDecisionId is null ? null : new ReviewDecisionId(document.ReviewDecisionId.Value),
            document.Metadata);
}
