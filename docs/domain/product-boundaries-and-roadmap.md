# Product Boundaries And Roadmap

This document captures the project decisions that define what Hearth Calendar is, what it is not, and how the first build should progress.

## Product Principle

Calendar clients submit intent. Hearth Calendar makes calendar decisions.

The app owns:

- calendar policy
- review workflow
- event metadata
- virtual calendars
- feeds
- audit trail
- integration credentials

External systems should not own family-specific calendar decisions.

## Explicit Boundaries

Hearth Calendar does not do:

- voice capture
- wake-word detection
- speech-to-text
- text-to-speech
- Home Assistant Assist conversation handling

Home Assistant remains responsible for voice and conversation capture. Hearth Calendar receives structured or semi-structured calendar intent from Home Assistant and decides what should happen.

## Storage Decision

The app uses PostgreSQL through Marten as the source of truth.

Baikal is not part of the new architecture.

Do not add:

- Baikal storage adapter
- Baikal migration layer
- CalDAV object mapping to Baikal
- Baikal as fallback source of truth

If CalDAV is implemented, it should be an app-owned protocol adapter over Hearth Calendar state.

## Inputs

Supported or planned inputs:

- Home Assistant-originated event intent
- web UI event creation
- CalDAV Smart Inbox writes
- future imports from external calendars

All inputs must pass through the same intake and review policy.

## Outputs

Supported or planned outputs:

- Blazor WebAssembly web UI
- review queue
- virtual calendar views
- read-only ICS feeds
- future read-only CalDAV virtual calendars
- Home Assistant notifications for relevant issues

Feeds and read-only calendar views are projections from approved app state.

## Virtual Calendars

Initial virtual calendars:

- Adult A
- Adult B
- Child
- Family
- Events
- Combined
- Review

Virtual calendars are app-owned views generated from event metadata. They are not storage buckets.

## First Useful Web Screens

Initial UI screens:

- Review queue
- Upcoming approved events
- Create event form
- Event detail/edit screen
- Audit log
- Credential/feed token management

The review queue is the primary human fallback when the app cannot safely decide.

## CalDAV Direction

The app should eventually expose its own CalDAV endpoints.

First CalDAV capability:

- one writable Smart Inbox calendar

Later CalDAV capability:

- read-only virtual calendars

Longer-term writable virtual calendars may be considered, but writes must still pass through app-owned intake, review, validation, clash detection, and audit.

## Feed Direction

The app should generate read-only ICS feeds:

- Combined
- Adult A
- Adult B
- Child
- Family
- Events

Feed rules:

- approved events only
- staged/rejected items excluded
- read-only tokens separate from write/admin credentials
- generated from Marten-backed app state

## First Milestone

Build the first vertical slice:

1. Define domain models:
   - `EventIntent`
   - `CalendarEvent`
   - `Participant`
   - `ReviewDecision`
   - `VirtualCalendar`
   - `AuditEntry`

2. Add solution/build foundation:
   - hosted Blazor WebAssembly solution
   - central package management
   - warnings as errors
   - test project
   - GitHub Actions CI

3. Implement deterministic review logic:
   - birthday and anniversary routing
   - personal and family routing
   - child responsibility inference
   - ambiguity handling
   - basic clash detection

4. Add no-op AI review provider:
   - provider abstraction exists
   - AI can be disabled
   - final decision remains app-owned

5. Add tests before implementation using red/green/refactor:
   - key routing examples
   - ambiguity staging
   - past event rules
   - clash detection
   - responsibility self-clash exclusion

6. Add persistence:
   - Marten documents for intents, decisions, events, audit, credentials
   - approved event queries
   - review queue queries

7. Add first intake endpoint:

```http
POST /api/intake/event
```

8. Add first web UI slice:
   - create event
   - review queue
   - upcoming events

9. Add SignalR notifications:
   - review queue changed
   - calendar events changed

10. Add read-only ICS feeds after approved event projections are stable.

## First Milestone Acceptance Criteria

- No external calendar server is required.
- CI restores, builds, and tests the solution on pull requests and pushes to `main`.
- The app can accept event intent through a server endpoint.
- The review pipeline can approve, reject, or stage an intent.
- Approved events are persisted in Marten.
- Review decisions and audit entries are persisted.
- Staged events appear in a review queue query.
- Approved events appear in upcoming event queries.
- AI review can be disabled without changing domain logic.
- Blazor WASM uses BluQube for web UI commands/queries.
- SignalR only notifies clients after persisted changes.
- Tests cover the first routing and clash behaviours.
- No real personal details appear in examples, tests, or sample config.
