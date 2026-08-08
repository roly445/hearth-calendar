# Hearth Calendar Domain Model

This document captures the first implementation-facing domain model. Names are C#-shaped, but the intent is more important than the exact syntax.

## Aggregate Candidates

### Event Intent

Represents what an external or internal actor asked the app to do.

```csharp
public sealed record EventIntent
{
    public required EventIntentId Id { get; init; }
    public required CalendarSource Source { get; init; }
    public required string RawText { get; init; }
    public IntentPayload? Payload { get; init; }
    public required Instant SubmittedAt { get; init; }
    public required ActorRef SubmittedBy { get; init; }
}
```

Notes:

- Home Assistant may submit mostly natural language plus a parsed date/time.
- Web UI may submit a richer structured payload.
- CalDAV may submit parsed iCalendar data.
- The review pipeline should handle all of these through the same `EventIntent` shape.

### Calendar Event

Represents an app-owned calendar item.

```csharp
public sealed record CalendarEvent
{
    public required CalendarEventId Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public required EventTime Time { get; init; }
    public RecurrenceRule? Recurrence { get; init; }
    public required IReadOnlyList<Participant> Participants { get; init; }
    public required VirtualCalendarId PrimaryCalendar { get; init; }
    public required EventCategory Category { get; init; }
    public required BusyStatus BusyStatus { get; init; }
    public ResponsibleAdult? ResponsibleAdult { get; init; }
    public required ReviewStatus ReviewStatus { get; init; }
    public required CalendarSource Source { get; init; }
    public required Instant CreatedAt { get; init; }
    public Instant? UpdatedAt { get; init; }
}
```

Important rule:

`PrimaryCalendar` is metadata used to generate virtual calendars. It is not a storage bucket.

### Review Decision

Represents the app's decision about an intent.

```csharp
public sealed record ReviewDecision
{
    public required ReviewDecisionId Id { get; init; }
    public required EventIntentId IntentId { get; init; }
    public CalendarEventId? CalendarEventId { get; init; }
    public required ReviewStatus Status { get; init; }
    public required DecisionMode Mode { get; init; }
    public required IReadOnlyList<DecisionReason> Reasons { get; init; }
    public required IReadOnlyList<Clash> Clashes { get; init; }
    public AiReviewSuggestionId? AiSuggestionId { get; init; }
    public required Instant DecidedAt { get; init; }
    public required ActorRef DecidedBy { get; init; }
}
```

Decision rules:

- `Approved` decisions may create approved calendar events.
- `Staged` decisions must appear in the review queue.
- `Rejected` decisions must not appear in approved calendars or feeds.
- AI suggestions can be linked, but the final decision remains app-owned.

### Audit Entry

Represents a durable audit fact.

```csharp
public sealed record AuditEntry
{
    public required AuditEntryId Id { get; init; }
    public required AuditAction Action { get; init; }
    public required ActorRef Actor { get; init; }
    public required Instant OccurredAt { get; init; }
    public required string Summary { get; init; }
    public EventIntentId? IntentId { get; init; }
    public CalendarEventId? CalendarEventId { get; init; }
    public ReviewDecisionId? ReviewDecisionId { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}
```

Audit is not logging. It is application state.

## Supporting Value Objects

### Household Metadata

Household people are metadata used by review rules, responsibility assignment, and virtual calendar projections.
They are not calendars and they are not storage buckets.

```csharp
public sealed record HouseholdMember
{
    public required HouseholdMemberId Id { get; init; }
    public required string DisplayName { get; init; }
    public required HouseholdMemberKind Kind { get; init; }
    public required bool IsActive { get; init; }
}

public sealed record HouseholdRelationship
{
    public required HouseholdMemberId From { get; init; }
    public required HouseholdMemberId To { get; init; }
    public required HouseholdRelationshipKind Kind { get; init; }
    public required bool IsActive { get; init; }
}
```

Initial implementation note:

- `adult-a`, `adult-b`, and `child` are generic compatibility defaults.
- `KnownPeople` is a bridge over default household metadata while persistence and admin UI are still future slices.
- Future calendar views should filter over household members, relationships, event categories, responsibilities, and tags.
- Committed examples and tests must use generic labels rather than real personal names.

### Event Time

```csharp
public sealed record EventTime
{
    public required LocalDate Date { get; init; }
    public LocalTime? StartTime { get; init; }
    public LocalTime? EndTime { get; init; }
    public required DateTimeZone TimeZone { get; init; }
    public required bool IsAllDay { get; init; }
}
```

Rules:

- All-day events should not fake midnight start/end times in the domain.
- Europe/London should be the default family timezone.
- External adapters can convert to and from iCalendar or API-specific formats.

### Participant

```csharp
public sealed record Participant
{
    public required PersonId PersonId { get; init; }
    public required ParticipationRole Role { get; init; }
    public required BusyStatus BusyStatus { get; init; }
}
```

### Responsible Adult

```csharp
public sealed record ResponsibleAdult
{
    public required PersonId AdultPersonId { get; init; }
    public required ResponsibilityKind Kind { get; init; }
    public required ResponsibilitySource Source { get; init; }
}
```

Rules:

- A Child event may have Adult A or Adult B as responsible adult.
- Responsibility should be represented as metadata on the Child event.
- Views may project adult responsibility items, but those must retain the parent event link.

### Clash

```csharp
public sealed record Clash
{
    public required CalendarEventId ConflictingEventId { get; init; }
    public required IReadOnlyList<PersonId> AffectedPeople { get; init; }
    public required ClashSeverity Severity { get; init; }
    public required string Summary { get; init; }
}
```

### AI Review Suggestion

```csharp
public sealed record AiReviewSuggestion
{
    public required AiReviewSuggestionId Id { get; init; }
    public required EventIntentId IntentId { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public string? SuggestedTitle { get; init; }
    public VirtualCalendarId? SuggestedCalendar { get; init; }
    public IReadOnlyList<PersonId> SuggestedParticipants { get; init; } =
        Array.Empty<PersonId>();
    public PersonId? SuggestedResponsibleAdult { get; init; }
    public RecurrenceRule? SuggestedRecurrence { get; init; }
    public required decimal Confidence { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required Instant CreatedAt { get; init; }
}
```

Rules:

- Suggestions are advisory.
- Suggestions should be persisted separately from final decisions.
- Provider errors should degrade to deterministic review.

## Enumerations

```csharp
public enum CalendarSource
{
    HomeAssistant,
    Web,
    CalDav,
    Import,
    Admin,
    Test
}

public enum VirtualCalendarId
{
    AdultA,
    AdultB,
    Child,
    Family,
    Events,
    Combined,
    Review
}

public enum HouseholdMemberKind
{
    Adult,
    Child
}

public enum HouseholdRelationshipKind
{
    PartnerOf,
    ParentOrGuardianOf,
    HouseholdMemberOf,
    ResponsibleFor
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

public enum DecisionMode
{
    Automatic,
    Manual,
    AssistedByAi
}
```

## Known People Seed Data

| Person | Initial ID | Notes |
| --- | --- | --- |
| Adult A | `adult-a` | Adult, can be responsible adult. |
| Adult B | `adult-b` | Adult, can be responsible adult. |
| Child | `child` | Child, has responsibility/chaperone rules. |

These should be seed data, not hard-coded throughout the domain. The first implementation can still use well-known IDs where it keeps tests clear.

## Virtual Calendar Rules

| Calendar | Rule |
| --- | --- |
| Adult A | Events where Adult A is a busy participant, plus projected responsibility items. |
| Adult B | Events where Adult B is a busy participant, plus projected responsibility items. |
| Child | Child's personal events and child-focused activities. |
| Family | Events involving Adult A, Adult B, and Child as a family unit. |
| Events | Non-busy reference events such as birthdays and anniversaries. |
| Combined | Approved events from all calendars. |
| Review | Staged items only. Never exported as an approved feed. |

## Invariants

- An approved event must have a title, time, primary calendar, category, and source.
- A staged event must have at least one reason explaining what needs review.
- A rejected event must have at least one reason explaining why it was rejected.
- Birthday and anniversary events must be non-busy.
- Birthday and anniversary events should have yearly recurrence when a date is known.
- A family event includes Adult A, Adult B, and Child unless explicitly modelled otherwise.
- A Child responsibility event must keep the Child event as the parent record.
- A projected responsibility item must not clash with its parent Child event.
- Feeds and approved calendar views must exclude staged and rejected events.
- Deletes must not succeed without an exact match.
- Reschedules must update an existing event when confidently matched and must not create adjacent duplicates.
- Every decision and mutation must create an audit entry.

## Service Interfaces

These are the initial domain-facing ports.

```csharp
public interface IEventReviewPipeline
{
    Task<ReviewDecision> ReviewAsync(
        EventIntent intent,
        CancellationToken cancellationToken);
}

public interface IAiReviewProvider
{
    Task<AiReviewSuggestion?> ReviewAsync(
        AiReviewRequest request,
        CancellationToken cancellationToken);
}

public interface ICalendarEventRepository
{
    Task<IReadOnlyList<CalendarEvent>> FindBusyEventsAsync(
        EventTime candidateTime,
        IReadOnlyList<PersonId> affectedPeople,
        CancellationToken cancellationToken);
}

public interface IAuditWriter
{
    Task RecordAsync(
        AuditEntry entry,
        CancellationToken cancellationToken);
}
```

## First Test Set

| Test | Expected Result |
| --- | --- |
| `Adult B birthday on 25 July` | Events calendar, non-busy, yearly recurrence, approved. |
| `Child swimming with Adult B` | Child calendar, Adult B responsible adult. |
| `Family BBQ` | Family calendar, all three participants. |
| `Dentist for Adult A` | Adult A calendar. |
| `dentist` | Staged for review. |
| Past non-birthday booking | Rejected or staged based on source mode. |
| Past birthday | Approved as yearly reference event. |
| Adult A and Family overlap | Clash detected. |
| Child event with managed Adult B responsibility | Parent/responsibility pair does not self-clash. |
| Duplicate-looking reschedule | Existing event updated; no adjacent duplicate. |
| Exact delete | Matching event deleted and audited. |
| Misheard delete | Not claimed as successful. |
| Read-only feed | Excludes staged review items. |
