namespace HearthCalendar.Shared.Domain;

public readonly record struct EventIntentId(Guid Value)
{
    public static EventIntentId New() => new(Guid.NewGuid());
}

public readonly record struct CalendarEventId(Guid Value)
{
    public static CalendarEventId New() => new(Guid.NewGuid());
}

public readonly record struct ReviewDecisionId(Guid Value)
{
    public static ReviewDecisionId New() => new(Guid.NewGuid());
}

public readonly record struct AuditEntryId(Guid Value)
{
    public static AuditEntryId New() => new(Guid.NewGuid());
}

public readonly record struct PersonId(string Value);

public sealed record Person(PersonId Id, string DisplayName, PersonKind Kind);

public sealed record EventIntent(
    EventIntentId Id,
    CalendarSource Source,
    ReviewSourceMode SourceMode,
    string RawText,
    EventIntentPayload? Payload,
    DateTimeOffset SubmittedAt,
    ActorRef SubmittedBy);

public sealed record EventIntentPayload(DateOnly? Date, TimeOnly? StartTime, TimeOnly? EndTime);

public sealed record CalendarEvent(
    CalendarEventId Id,
    string Title,
    EventTime Time,
    VirtualCalendar PrimaryCalendar,
    EventCategory Category,
    BusyStatus BusyStatus,
    IReadOnlyList<Participant> Participants,
    CalendarSource Source,
    ReviewStatus ReviewStatus,
    RecurrenceRule? Recurrence = null,
    ResponsibleAdult? ResponsibleAdult = null,
    CalendarEventId? ParentEventId = null)
{
    public static CalendarEvent Approved(
        CalendarEventId id,
        string title,
        EventTime time,
        VirtualCalendar primaryCalendar,
        EventCategory category,
        BusyStatus busyStatus,
        IReadOnlyList<Participant> participants,
        CalendarSource source,
        RecurrenceRule? recurrence = null,
        ResponsibleAdult? responsibleAdult = null,
        CalendarEventId? parentEventId = null) =>
        new(
            id,
            title,
            time,
            primaryCalendar,
            category,
            busyStatus,
            participants,
            source,
            ReviewStatus.Approved,
            recurrence,
            responsibleAdult,
            parentEventId);
}

public sealed record Participant(Person Person, ParticipationRole Role, BusyStatus BusyStatus);

public sealed record ResponsibleAdult(Person Adult, ResponsibilityKind Kind, ResponsibilitySource Source);

public sealed record EventTime(DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, bool IsAllDay)
{
    public bool Overlaps(EventTime other)
    {
        if (Date != other.Date)
        {
            return false;
        }

        if (IsAllDay || other.IsAllDay)
        {
            return true;
        }

        var start = StartTime ?? TimeOnly.MinValue;
        var end = EndTime ?? TimeOnly.MaxValue;
        var otherStart = other.StartTime ?? TimeOnly.MinValue;
        var otherEnd = other.EndTime ?? TimeOnly.MaxValue;

        return start < otherEnd && otherStart < end;
    }
}

public sealed record RecurrenceRule(RecurrenceFrequency Frequency);

public sealed record ReviewDecision(
    ReviewDecisionId Id,
    EventIntentId IntentId,
    ReviewStatus Status,
    DecisionMode Mode,
    IReadOnlyList<DecisionReason> Reasons,
    IReadOnlyList<Clash> Clashes,
    CalendarEvent? Event,
    DateTimeOffset DecidedAt,
    ActorRef DecidedBy);

public sealed record ReviewOutcome(ReviewDecision Decision, AuditEntry AuditEntry);

public sealed record DecisionReason(DecisionReasonCode Code, string Message);

public sealed record Clash(
    CalendarEventId ConflictingEventId,
    IReadOnlyList<Person> AffectedPeople,
    ClashSeverity Severity,
    string Summary);

public sealed record AuditEntry(
    AuditEntryId Id,
    AuditAction Action,
    ActorRef Actor,
    DateTimeOffset OccurredAt,
    string Summary,
    EventIntentId? IntentId = null,
    CalendarEventId? CalendarEventId = null,
    ReviewDecisionId? ReviewDecisionId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ActorRef(string Id)
{
    public static ActorRef System { get; } = new("system");
}

public enum CalendarSource
{
    HomeAssistant,
    Web,
    CalDav,
    Import,
    Admin,
    Test
}

public enum ReviewSourceMode
{
    Passive,
    Interactive
}

public enum VirtualCalendar
{
    AdultA,
    AdultB,
    Child,
    Family,
    Events,
    Combined,
    Review
}

public enum EventCategory
{
    Personal,
    Family,
    Birthday,
    Anniversary,
    Responsibility,
    Reference,
    Unknown
}

public enum ReviewStatus
{
    Approved,
    Staged,
    Rejected
}

public enum BusyStatus
{
    Busy,
    Free
}

public enum ParticipationRole
{
    Attendee,
    Owner,
    Child,
    ResponsibleAdult
}

public enum ResponsibilityKind
{
    Taking,
    Collecting,
    Chaperoning,
    Attending,
    Unknown
}

public enum ResponsibilitySource
{
    Inferred,
    Manual
}

public enum DecisionMode
{
    Automatic,
    Manual,
    AssistedByAi
}

public enum RecurrenceFrequency
{
    Yearly
}

public enum ClashSeverity
{
    Warning,
    Blocking
}

public enum DecisionReasonCode
{
    DeterministicMatch,
    AmbiguousIntent,
    MissingDate,
    PastEvent,
    ClashDetected
}

public enum PersonKind
{
    Adult,
    Child
}

public enum AuditAction
{
    IntentReviewed,
    EventApproved,
    EventStaged,
    EventRejected
}
