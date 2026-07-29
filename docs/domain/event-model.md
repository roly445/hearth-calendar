# Hearth Calendar Event Model

This document describes the first domain model for Hearth Calendar using an event modelling style.

The model is intentionally centred on user-visible outcomes:

- calendar intent is submitted
- the app interprets and reviews it
- the app approves, rejects, or stages it
- approved events appear in calendars and feeds
- every meaningful decision is auditable

## Modelling Principles

- PostgreSQL, through Marten, is the source of truth.
- Home Assistant, web UI, CalDAV clients, and future imports are intake adapters.
- ICS and virtual calendars are projections from approved app state.
- AI review is a replaceable suggestion provider, not the decision authority.
- Deterministic rules own final safety decisions such as clashes, deletes, reschedules, recurrence, and permissions.

## System Context

```mermaid
flowchart LR
    HA["Home Assistant"] --> Intake["App Intake"]
    Web["Web UI"] --> Intake
    CalDAV["CalDAV Clients"] --> Intake
    Imports["Future Imports"] --> Intake

    Intake --> Review["Review Pipeline"]
    Review --> Store[("Marten / PostgreSQL")]
    Store --> Views["Virtual Calendar Projections"]
    Store --> Queue["Review Queue"]
    Store --> Audit["Audit Log"]

    Views --> ICS["Read-only ICS Feeds"]
    Views --> UI["Calendar UI"]
    Views --> CalDAVRead["Read-only CalDAV Calendars"]
```

## Event Model Legend

The flows below use these concepts:

- **Command**: a request to do something.
- **Domain Event**: something important that happened.
- **Policy**: logic that reacts to events and decides next commands.
- **Read Model**: queryable view used by UI, feeds, or integrations.
- **External System**: source or consumer outside the app.

## Core Event Timeline

```mermaid
sequenceDiagram
    participant Client as Client / Adapter
    participant Intake as Intake Endpoint
    participant Review as Review Pipeline
    participant Ai as AI Review Provider
    participant Store as Marten / PostgreSQL
    participant Projection as Projections

    Client->>Intake: SubmitEventIntent
    Intake->>Store: EventIntentSubmitted
    Intake->>Review: ReviewEventIntent
    Review->>Ai: RequestAiReviewSuggestion (optional)
    Ai-->>Review: AiReviewSuggestionReturned
    Review->>Store: ReviewDecisionRecorded

    alt approved
        Review->>Store: CalendarEventApproved
        Store->>Projection: Update approved event views
    else staged
        Review->>Store: EventStagedForManualReview
        Store->>Projection: Update review queue
    else rejected
        Review->>Store: EventRejected
        Store->>Projection: Update audit/rejection views
    end
```

## Primary Workflow: Submit Event Intent

This is the most important first vertical slice.

```mermaid
flowchart LR
    C1["Command: SubmitEventIntent"] --> E1["Event: EventIntentSubmitted"]
    E1 --> P1["Policy: Normalize Intent"]
    P1 --> E2["Event: EventIntentNormalized"]
    E2 --> P2["Policy: Deterministic Review"]
    E2 --> P3["Policy: Optional AI Suggestion"]
    P3 --> E3["Event: AiReviewSuggestionRecorded"]
    E3 --> P4["Policy: Merge Suggestions Safely"]
    P2 --> P4
    P4 --> P5["Policy: Clash / Safety Review"]
    P5 --> D1{"Decision"}
    D1 --> E4["Event: EventApproved"]
    D1 --> E5["Event: EventStagedForReview"]
    D1 --> E6["Event: EventRejected"]
    E4 --> R1["Read Model: Upcoming Events"]
    E4 --> R2["Read Model: Virtual Calendars"]
    E5 --> R3["Read Model: Review Queue"]
    E4 --> R4["Read Model: Audit Log"]
    E5 --> R4
    E6 --> R4
```

### Commands

| Command | Actor | Purpose |
| --- | --- | --- |
| `SubmitEventIntent` | Home Assistant, web UI, CalDAV, import | Submit raw or structured calendar intent for app review. |
| `ReviewEventIntent` | System | Run the deterministic and optional AI-assisted review pipeline. |
| `ApproveReviewedEvent` | Admin/user/system | Approve a staged or automatically approved event. |
| `RejectReviewedEvent` | Admin/user/system | Reject an unsafe or unwanted intent. |
| `EditReviewedEvent` | Admin/user | Modify staged event details before approval. |

### Domain Events

| Event | Meaning |
| --- | --- |
| `EventIntentSubmitted` | An adapter submitted calendar intent. |
| `EventIntentNormalized` | The app parsed dates, title, source metadata, and obvious structure. |
| `AiReviewSuggestionRecorded` | A configured AI provider supplied a suggestion. |
| `ReviewDecisionRecorded` | The app decided approved, staged, or rejected with reasons. |
| `CalendarEventApproved` | A calendar event became part of approved app state. |
| `EventStagedForManualReview` | The app could not safely decide automatically. |
| `EventRejected` | The app refused the intent. |
| `AuditEntryRecorded` | A durable audit entry was written. |

### Read Models

| Read Model | Used By | Contains |
| --- | --- | --- |
| `ReviewQueue` | Web UI | Staged intents, suggestions, clashes, and review actions. |
| `UpcomingEvents` | Web UI | Approved future events across selected calendars. |
| `VirtualCalendar` | UI, ICS, CalDAV | Approved events filtered by metadata. |
| `AuditTrail` | Web UI/admin | Decision and change history. |
| `FeedIndex` | ICS endpoints | Active feeds and token metadata. |

## Review Pipeline

```mermaid
flowchart TD
    A["EventIntentSubmitted"] --> B["Normalize title, date, source, actor"]
    B --> C["Detect birthday / anniversary"]
    B --> D["Detect named people"]
    B --> E["Detect family wording"]
    B --> F["Detect Child responsibility wording"]
    C --> G["Build deterministic suggestion"]
    D --> G
    E --> G
    F --> G
    B --> H["Optional AI provider"]
    H --> I["AI suggestion"]
    G --> J["Merge suggestions under safety rules"]
    I --> J
    J --> K["Validate recurrence and past date policy"]
    K --> L["Check clashes"]
    L --> M{"Can safely approve?"}
    M -->|yes| N["Record approved decision"]
    M -->|needs human| O["Stage for manual review"]
    M -->|unsafe| P["Reject"]
```

### Review Statuses

| Status | Meaning |
| --- | --- |
| `Approved` | The app has enough confidence and no blocking safety issue. |
| `Staged` | Human review is needed before the event becomes visible in calendars/feeds. |
| `Rejected` | The request is unsafe, invalid, unauthorized, or impossible. |

## Policy Matrix

| Input Pattern | Policy | Expected Result |
| --- | --- | --- |
| `Adult B birthday on 25 July` | Birthday routing | Events calendar, yearly recurrence, non-busy, approved. |
| `Our anniversary` | Anniversary routing | Events calendar, yearly recurrence, non-busy, approved or staged if date missing. |
| `Dentist for Adult A` | Personal routing | Adult A calendar, busy, clash checked. |
| `Family BBQ` | Family routing | Family calendar, Adult A/Adult B/Child participants, clash checked. |
| `Child swimming with Adult B` | Child responsibility | Child calendar, Adult B responsible adult, responsibility shadow considered. |
| `dentist` | Ambiguity handling | Staged for review. |
| Past non-reference event | Past event policy | Rejected or staged based on source mode. |
| Past birthday | Reference recurrence policy | Allowed if yearly recurrence can be inferred. |

## Child Responsibility Flow

```mermaid
flowchart LR
    C["Command: SubmitEventIntent"] --> E["EventIntentSubmitted"]
    E --> P["Policy: Infer Child Responsibility"]
    P --> D{"Responsible adult clear?"}
    D -->|yes| A["Event: ResponsibleAdultInferred"]
    D -->|no| S["Event: EventStagedForManualReview"]
    A --> CE["Event: CalendarEventApproved"]
    CE --> RM1["Read Model: Child Calendar"]
    CE --> RM2["Read Model: Adult Responsibility View"]
    CE --> CL["Policy: Clash Detection ignores own managed responsibility"]
```

The responsibility link is domain metadata, not a separate source-of-truth event. Projections may render it as an adult responsibility item where useful, but the relationship must remain traceable to the original Child event.

## Clash Detection Flow

```mermaid
flowchart TD
    A["Candidate CalendarEvent"] --> B["Resolve affected people"]
    B --> C["Load busy approved events in range"]
    C --> D["Exclude non-busy reference events"]
    D --> E["Exclude own managed responsibility links"]
    E --> F{"Conflicts remain?"}
    F -->|no| G["No clash warning"]
    F -->|yes| H["ClashDetected"]
    H --> I{"Source mode"}
    I -->|interactive| J["Reject or ask for confirmation"]
    I -->|passive| K["Stage for review"]
```

## Delete Flow

Deletes must be exact and auditable.

```mermaid
flowchart LR
    C["Command: RequestEventDelete"] --> M["Policy: Match Event"]
    M --> D{"Exact match?"}
    D -->|yes| E["Event: CalendarEventDeleted"]
    D -->|no| S["Event: DeleteStagedForReview"]
    D -->|unsafe| R["Event: DeleteRejected"]
    E --> A["AuditEntryRecorded"]
    S --> A
    R --> A
```

## Reschedule Flow

```mermaid
flowchart LR
    C["Command: RequestEventReschedule"] --> M["Policy: Match Existing Event"]
    M --> D{"Confident match?"}
    D -->|yes| V["Policy: Validate New Time"]
    D -->|no| S["Event: RescheduleStagedForReview"]
    V --> CL["Policy: Clash Detection"]
    CL --> E["Event: CalendarEventRescheduled"]
    CL --> S
    E --> A["AuditEntryRecorded"]
    S --> A
```

## AI Review Provider Boundary

```mermaid
flowchart LR
    Review["Review Pipeline"] --> Contract["IAiReviewProvider"]
    Contract --> OpenAI["OpenAI Provider"]
    Contract --> Other["Other Provider"]
    Contract --> Local["Local Provider"]
    Contract --> NoOp["No-op Provider"]

    OpenAI --> Suggestion["AiReviewSuggestion"]
    Other --> Suggestion
    Local --> Suggestion
    NoOp --> Suggestion

    Suggestion --> Safety["App Safety Rules"]
    Safety --> Decision["App-owned ReviewDecision"]
```

AI review can suggest:

- normalized title
- participants
- target calendar
- responsible adult
- recurrence hint
- ambiguity warnings
- confidence and reasons

AI review must not decide:

- final approval
- clash safety
- authorization
- exact delete matching
- exact reschedule matching
- whether an audit entry is required

## Event Streams And Documents

The first implementation can use Marten document storage with explicit audit entries. Event sourcing can be introduced later if the lifecycle becomes complex enough.

Recommended initial documents:

| Document | Purpose |
| --- | --- |
| `EventIntentDocument` | Submitted raw/structured intent. |
| `CalendarEventDocument` | Approved or staged calendar event candidate. |
| `ReviewDecisionDocument` | App-owned decision and reasons. |
| `AiReviewSuggestionDocument` | Provider-specific suggestion output. |
| `AuditEntryDocument` | Append-only audit trail. |
| `ClientCredentialDocument` | Per-adapter/client credentials. |
| `FeedTokenDocument` | Read-only feed tokens. |

Potential future event streams:

| Stream | Events |
| --- | --- |
| `intent-{id}` | Submitted, normalized, reviewed. |
| `event-{id}` | Approved, edited, rescheduled, deleted. |
| `review-{id}` | Staged, approved, rejected, comments added. |
| `credential-{id}` | Created, rotated, revoked. |

## First Slice

The first build slice should prove this path:

```text
POST /api/intake/event
  -> EventIntentSubmitted
  -> deterministic review
  -> optional no-op AI suggestion
  -> ReviewDecisionRecorded
  -> CalendarEventApproved or EventStagedForManualReview
  -> audit entry
  -> upcoming events or review queue read model
```

Acceptance criteria:

- The app can accept an event intent without any external calendar dependency.
- Birthday, personal, family, Child responsibility, ambiguity, and clash examples are covered by tests.
- AI review can be disabled or swapped without changing domain logic.
- Approved events are queryable by virtual calendar.
- Staged events appear in the review queue.
- Rejected events do not appear in feeds or approved calendar views.
