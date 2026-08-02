# GitHub Issue Backlog

These issues have been created in `roly445/hearth-calendar`.

## Created Issues

| Phase | Issue |
| --- | --- |
| Phase 0: Scaffold hosted Blazor WASM .NET solution | [#1](https://github.com/roly445/hearth-calendar/issues/1) |
| Phase 1: Implement calendar brain domain model and deterministic review | [#2](https://github.com/roly445/hearth-calendar/issues/2) |
| Phase 2: Add plug-and-play AI review abstraction | [#3](https://github.com/roly445/hearth-calendar/issues/3) |
| Phase 3: Add Marten/PostgreSQL persistence and audit | [#4](https://github.com/roly445/hearth-calendar/issues/4) |
| Phase 4: Add auth stack and intake APIs | [#5](https://github.com/roly445/hearth-calendar/issues/5) |
| Phase 5: Build first Blazor WASM/BluQube UI slice | [#6](https://github.com/roly445/hearth-calendar/issues/6) |
| Phase 6: Add PWA installability and offline caching | [#7](https://github.com/roly445/hearth-calendar/issues/7) |
| Phase 7: Add read-only ICS feeds | [#8](https://github.com/roly445/hearth-calendar/issues/8) |
| Phase 8: Add delete and reschedule safety flows | [#9](https://github.com/roly445/hearth-calendar/issues/9) |
| Phase 9: Spike and implement app-owned CalDAV | [#10](https://github.com/roly445/hearth-calendar/issues/10) |

## Ordering

Critical path:

```text
#1 -> #2 -> #4 -> #5 -> #6
```

Parallel or semi-parallel work:

```text
#3 can start after #2
#8 can start after #4 and #5
#9 can start after #2 and #4
#7 can start after #6
#10 should wait for #5 and #9
```

Each GitHub issue includes a `Dependencies` section with clickable blocker links.

## Issue 1: Phase 0: Scaffold hosted Blazor WASM .NET solution

```md
## Goal

Create the initial .NET solution skeleton for Hearth Calendar.

## Scope

- Hosted Blazor WebAssembly solution shape.
- ASP.NET Core server project.
- Blazor WASM client project.
- Client-owned BluQube contracts and DTOs.
- Test project.
- Central package management.
- Warnings as errors.
- GitHub Actions CI.
- Basic health endpoint.

## Acceptance Criteria

- Solution contains `HearthCalendar.Server`, `HearthCalendar.Client`, and tests.
- `dotnet build` passes with warnings as errors.
- Tests run from the command line.
- CI runs on pull requests targeting `main`.
- CI runs on pushes to `main`.
- CI restores, builds, and runs tests.
- CI does not require real secrets for normal build/test.
- Package versions are centralized.
- Server exposes an explicitly anonymous health endpoint.
- DI validation is enabled in development/test environments.
- No real personal details appear in code, docs, tests, screenshots, or sample config.

## Tests

- Add a basic smoke test for app startup/health endpoint if practical.

## Notes

Follow `docs/engineering/dotnet-app-defaults-compliance.md` and `docs/engineering/bluqube-usage.md`.
Branch protection should be configured after the CI workflow exists and the check name is visible in GitHub.
```

## Issue 2: Phase 1: Implement calendar brain domain model and deterministic review

```md
## Goal

Build the first testable calendar brain without depending on UI, Marten, Home Assistant, CalDAV, or AI providers.

## Scope

- Core domain types.
- Deterministic review pipeline.
- Birthday and anniversary routing.
- Personal and family routing.
- Child responsibility inference.
- Ambiguity staging.
- Basic clash detection.

## Acceptance Criteria

- Domain includes `EventIntent`, `CalendarEvent`, `Participant`, `Person`, `VirtualCalendar`, `ReviewDecision`, `RecurrenceRule`, and `AuditEntry`.
- Birthday/anniversary examples route to Events, are non-busy, and get yearly recurrence when date is known.
- Personal examples route to the named person calendar.
- Family examples route to Family and include both adults plus child.
- Child responsibility examples record responsible adult where clear.
- Ambiguous input is staged rather than guessed.
- Clash detection finds relevant busy conflicts and ignores non-busy reference events.
- Responsibility projections do not self-clash with their parent child event.
- No real personal details appear in examples or tests.

## Tests

Use red/green/refactor. Start each new behaviour with a failing test unless explicitly impractical.

Required tests:

- `Adult B birthday on 25 July` routes to Events and recurs yearly.
- `Child swimming with Adult B` routes to Child and records Adult B as responsible adult.
- `Family BBQ` routes to Family.
- `Dentist for Adult A` routes to Adult A.
- `dentist` stages for review.
- Past non-birthday event is rejected or staged according to source mode.
- Past birthday is allowed as yearly reference event.
- Adult A and Family overlap produces a clash.
- Child event plus managed adult responsibility does not self-clash.
```

## Issue 3: Phase 2: Add plug-and-play AI review abstraction

```md
## Goal

Add a provider-agnostic AI review boundary while keeping final decisions app-owned.

## Scope

- `IAiReviewProvider` contract.
- Structured AI review request/result DTOs.
- No-op provider.
- Safe merge policy for AI suggestions.
- Persistence shape for AI suggestions later.

## Acceptance Criteria

- App can run with AI disabled.
- Review pipeline depends only on `IAiReviewProvider`, not a provider SDK.
- AI suggestions can include normalized title, participants, target calendar, responsible adult, recurrence hint, confidence, and reasons.
- AI cannot override deterministic auth, clash, delete, reschedule, or recurrence safety rules.
- Provider errors degrade gracefully to deterministic/staged review.
- AI suggestions are distinguishable from final `ReviewDecision`.
- No unnecessary private data is included in provider request payloads.

## Tests

- No-op provider leaves deterministic review behaviour unchanged.
- Low-confidence suggestions are not applied automatically.
- High-confidence suggestions can resolve allowed ambiguity.
- Deterministic clash rejection/staging wins over AI approval-like suggestion.
- Provider failure does not fail the whole review pipeline.
```

## Issue 4: Phase 3: Add Marten/PostgreSQL persistence and audit

```md
## Goal

Persist app-owned calendar state in PostgreSQL through Marten.

## Scope

- Marten configuration.
- Typed database config.
- Documents for intents, decisions, events, AI suggestions, audit entries, credentials, feed tokens.
- Approved event queries.
- Review queue queries.
- Durable audit entries.

## Acceptance Criteria

- PostgreSQL/Marten is the source of truth.
- No external calendar server is required.
- Submitted intents are stored with source, raw payload/text, actor/client, and timestamp.
- Review decisions are stored with status, reasons, warnings/clashes, timestamps, and linked AI suggestion when present.
- Approved events are queryable by date range and virtual calendar.
- Staged/rejected items do not appear in approved event queries.
- Audit entries are written for meaningful state mutations.
- Raw credentials/tokens are never stored.

## Tests

- Integration tests use real Marten/PostgreSQL behaviour.
- Save/load intent document.
- Save/load review decision.
- Approved event query excludes staged/rejected items.
- Audit entry is written for approve/reject/edit/delete/reschedule paths where implemented.
```

## Issue 5: Phase 4: Add auth stack and intake APIs

```md
## Goal

Add secure-by-default auth and the first intake endpoints.

## Scope

- ASP.NET Core fallback authorization.
- Admin cookie auth shape.
- Client token auth for intake.
- Feed token validation shape.
- Home Assistant intake endpoint.
- Generic event intake endpoint.
- Auth audit events.

## Acceptance Criteria

- New endpoints require authorization by default.
- `GET /health` is explicitly anonymous.
- Admin endpoints require authenticated admin cookie/session.
- `POST /api/intake/event` requires `intake:write`.
- `POST /api/intake/home-assistant/event` requires `intake:write`.
- Feed tokens cannot call write endpoints.
- Write tokens cannot call admin endpoints.
- Unauthorized requests return `401` or `403` without leaking event/calendar existence.
- Auth-sensitive actions are audited without raw secrets.

## Tests

- Valid intake token can submit intent.
- Invalid token is rejected.
- Feed token cannot submit intent.
- Write token cannot access admin/BluQube UI commands.
- Health endpoint is anonymous.
```

## Issue 6: Phase 5: Build first Blazor WASM/BluQube UI slice

```md
## Goal

Build the first useful web UI using Blazor WASM and BluQube.

## Scope

- Review queue screen.
- Upcoming events screen.
- Create event screen.
- Initial event detail screen if practical.
- BluQube commands/queries for UI interactions.
- SignalR notifications for state changes.

## Acceptance Criteria

- Blazor WASM client uses `ICommandRunner` and `IQueryRunner`.
- BluQube commands/queries/results live in the client project.
- Handlers/processors/validators/authorizers live on the server.
- Review queue shows staged items, reasons, suggestions, and clashes.
- User can approve, edit, or reject staged items.
- Upcoming events show approved items only.
- Create event uses the same review pipeline.
- SignalR hub only publishes notifications after persistence succeeds.
- Clients refresh through BluQube queries after SignalR messages.

## Tests

- Handler/processor tests follow red/green/refactor.
- Authorization tests cover protected UI commands/queries.
- SignalR publishing is tested at service boundary.
```

## Issue 7: Phase 6: Add PWA installability and offline caching

```md
## Goal

Make the Blazor WASM client installable as a PWA with safe offline behaviour.

## Scope

- PWA manifest.
- Service worker app shell caching.
- Cached read-only views.
- Local outbox for safe new event intents.
- Reconnect/sync flow.
- Offline/stale UI indicators.

## Acceptance Criteria

- App can be installed as a PWA.
- App shell opens offline.
- Upcoming events/recent detail snapshots can be viewed offline as stale data.
- New event intent can be queued offline.
- Queued intent syncs when online.
- Offline queued items are not shown as approved until server review completes.
- Approve/reject/delete/reschedule require online fresh server state.
- Offline caches do not contain raw credentials or feed tokens.

## Tests

- Unit tests for local outbox state transitions where practical.
- Manual/browser verification for installability, offline launch, reconnect, and queued intent sync.
```

## Issue 8: Phase 7: Add read-only ICS feeds

```md
## Goal

Generate read-only ICS feeds from approved app state.

## Scope

- Combined feed.
- Adult A feed.
- Adult B feed.
- Child feed.
- Family feed.
- Events feed.
- Feed token validation.
- ICS compatibility tests.

## Acceptance Criteria

- Feed endpoints require valid feed tokens.
- Tokens are scoped to allowed virtual calendars.
- Feeds include approved events only.
- Staged/rejected items are excluded.
- Birthday/anniversary reference events export as non-busy where supported.
- All-day and recurring events export correctly.
- Feed output is generated from Marten-backed state.

## Tests

- ICS parser validates generated feeds.
- Snapshot tests protect expected feed shape.
- Recurring birthday exports correctly.
- All-day events export correctly.
- Invalid token is rejected.
- Token for one feed cannot read another feed.
```

## Issue 9: Phase 8: Add delete and reschedule safety flows

```md
## Goal

Implement safe delete and reschedule workflows.

## Scope

- Exact-match delete policy.
- Reschedule matching policy.
- Clash checks on reschedule.
- Staging for ambiguous delete/reschedule requests.
- Audit entries.

## Acceptance Criteria

- Delete succeeds only for exact event matches.
- Ambiguous delete is staged or rejected.
- App never reports delete success unless the event was actually removed from approved state.
- Reschedule updates an existing event where confidently matched.
- Duplicate-looking reschedule does not create adjacent duplicate events.
- Reschedule runs clash checks against the new time.
- Delete/reschedule actions write audit entries.

## Tests

- Exact delete works.
- Misheard/ambiguous delete does not claim success.
- Confident reschedule updates existing event.
- Duplicate-looking reschedule avoids adjacent duplicate.
- Clashing reschedule is rejected or staged according to source mode.
```

## Issue 10: Phase 9: Spike and implement app-owned CalDAV

```md
## Goal

Explore and then implement app-owned CalDAV support over Hearth Calendar state.

## Scope

- CalDAV compatibility spike.
- Writable Smart Inbox calendar.
- Future read-only virtual calendars.
- CalDAV app-password auth.
- Intake/review integration.

## Acceptance Criteria

- Spike documents minimum operations needed by OneCalendar/DAVx5.
- Prototype can accept a basic event write.
- Written event becomes an app `EventIntent`.
- Writable Smart Inbox writes pass through intake/review/audit.
- Approved writes become approved app events.
- Ambiguous writes become staged review items.
- Read-only virtual calendars exclude staged/rejected items.
- CalDAV credentials are separate from feed/admin tokens.

## Tests

- Protocol-level tests or compatibility fixtures where practical.
- Auth tests for CalDAV read/write scopes.
- Intake tests for CalDAV-created intent.
```
