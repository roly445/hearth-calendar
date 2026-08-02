using HearthCalendar.Server.Domain;

namespace HearthCalendar.Server.Persistence;

public sealed record EventIntentDocument
{
    public required Guid Id { get; init; }

    public required string Source { get; init; }

    public required string SourceMode { get; init; }

    public required string RawText { get; init; }

    public IntentPayloadDocument? Payload { get; init; }

    public required DateTimeOffset SubmittedAt { get; init; }

    public required string SubmittedBy { get; init; }
}

public sealed record IntentPayloadDocument(DateOnly? Date, TimeOnly? StartTime, TimeOnly? EndTime);

public sealed record CalendarEventDocument
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required EventTimeDocument Time { get; init; }

    public required string PrimaryCalendar { get; init; }

    public required string Category { get; init; }

    public required string BusyStatus { get; init; }

    public required IReadOnlyList<ParticipantDocument> Participants { get; init; }

    public required string Source { get; init; }

    public required string ReviewStatus { get; init; }

    public RecurrenceRuleDocument? Recurrence { get; init; }

    public ResponsibleAdultDocument? ResponsibleAdult { get; init; }

    public Guid? ParentEventId { get; init; }
}

public sealed record EventTimeDocument(DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, bool IsAllDay);

public sealed record ParticipantDocument(string PersonId, string DisplayName, string Kind, string Role, string BusyStatus);

public sealed record RecurrenceRuleDocument(string Frequency);

public sealed record ResponsibleAdultDocument(string AdultPersonId, string DisplayName, string Kind, string Source);

public sealed record ReviewDecisionDocument
{
    public required Guid Id { get; init; }

    public required Guid IntentId { get; init; }

    public required string Status { get; init; }

    public required string Mode { get; init; }

    public required IReadOnlyList<DecisionReasonDocument> Reasons { get; init; }

    public required IReadOnlyList<ClashDocument> Clashes { get; init; }

    public Guid? CalendarEventId { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public required string DecidedBy { get; init; }

    public Guid? AiSuggestionId { get; init; }
}

public sealed record DecisionReasonDocument(string Code, string Message);

public sealed record ClashDocument(Guid ConflictingEventId, IReadOnlyList<string> AffectedPersonIds, string Severity, string Summary);

public sealed record AiReviewSuggestionDocument
{
    public required Guid Id { get; init; }

    public required Guid IntentId { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public string? SuggestedTitle { get; init; }

    public string? SuggestedCalendar { get; init; }

    public required IReadOnlyList<string> SuggestedParticipants { get; init; }

    public string? SuggestedResponsibleAdult { get; init; }

    public RecurrenceRuleDocument? SuggestedRecurrence { get; init; }

    public required decimal Confidence { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record AuditEntryDocument
{
    public required Guid Id { get; init; }

    public required string Action { get; init; }

    public required string Actor { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string Summary { get; init; }

    public Guid? IntentId { get; init; }

    public Guid? CalendarEventId { get; init; }

    public Guid? ReviewDecisionId { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ClientCredentialDocument
{
    public required Guid Id { get; init; }

    public required string ClientName { get; init; }

    public required string SecretHash { get; init; }

    public required IReadOnlyList<string> Scopes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }
}

public sealed record FeedTokenDocument
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string TokenHash { get; init; }

    public required IReadOnlyList<string> AllowedCalendars { get; init; }

    public required IReadOnlyList<string> Scopes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }
}

public sealed record CalDavCredentialDocument
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string SecretHash { get; init; }

    public required IReadOnlyList<string> WritableCalendars { get; init; }

    public required IReadOnlyList<string> Scopes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }
}

public sealed record CalDavObjectDocument
{
    public required string Id { get; init; }

    public required string CalendarId { get; init; }

    public required string ItemId { get; init; }

    public required Guid IntentId { get; init; }

    public required string ContentHash { get; init; }

    public required string ETag { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
