# .NET App Defaults Compliance

Hearth Calendar should follow the local `dotnet-app-defaults` skill for all .NET implementation work.

This document translates that guidance into concrete project rules for this app.

## Scope

These standards apply to:

- ASP.NET Core server code
- Blazor WebAssembly client code
- shared BluQube contracts
- Marten/PostgreSQL infrastructure
- tests
- build configuration
- authentication and authorization code

## Architecture

Adopt now:

- Use a hosted Blazor WebAssembly solution:
  - `HearthCalendar.Server`
  - `HearthCalendar.Client`
  - `HearthCalendar.Shared`
  - test project or projects under `tests/`
- Prefer feature-oriented folders over horizontal technical buckets.
- Keep BluQube request/result contracts in shared code.
- Keep BluQube handlers, processors, validators, authorizers, Marten access, ASP.NET middleware, and SignalR hubs on the server.
- Keep domain/review logic plain and testable.
- Keep external adapters outside core domain policy.

Avoid:

- adding `.Data`, `.Business`, `.Common`, `.Models`, and `.Api` projects by habit
- introducing MediatR or another mediator alongside BluQube
- letting Home Assistant, CalDAV, ICS, AI, or UI code own calendar decisions
- moving server-only dependencies into the WASM client

## Build Quality

Adopt now:

- Treat compiler warnings as errors.
- Use nullable reference types.
- Use implicit usings where they reduce noise.
- Use central package management with `Directory.Packages.props`.
- Enable dependency injection validation in development and test environments.
- Validate important configuration at startup.
- Keep solution/project files modern SDK-style.

Acceptance criteria:

- `dotnet build` passes with warnings as errors.
- No new warning suppressions are added without a documented reason.
- Package versions are centralized.
- Invalid DI graphs fail during development/test startup.
- Missing required config fails with a clear message.

## Validation

Adopt now:

- Use FluentValidation for non-trivial commands and request models.
- Keep validators close to the feature they protect.
- Use BluQube command validation for UI commands.
- Validate queries inside query processors where needed.
- Keep validation separate from domain safety decisions.

Examples:

- Input validation checks required title/date/source fields.
- Domain policy decides whether a delete is exact enough.
- Domain policy decides whether a clash blocks approval.
- Auth decides whether a caller may request an action.

## Configuration

Adopt now:

- Use typed configuration for:
  - database/Marten
  - auth/cookies/tokens
  - AI review providers
  - CORS/CSP
  - feed settings
  - PWA/offline settings where server-controlled
- Keep secrets out of source control.
- Prefer simple typed config objects when reload/lifecycle behaviour is not needed.
- Use options abstractions only where they add value.

Acceptance criteria:

- Required settings are documented in sample config.
- Real secrets are not committed.
- Important config is validated at startup.
- Config keys are not scattered through application code.

## Security

Adopt now:

- Require authorization by default for ASP.NET Core endpoints.
- Require authorization by default for BluQube commands and queries.
- Make anonymous endpoints explicit.
- Use separate auth lanes for:
  - admin cookie sessions
  - client write tokens
  - feed read tokens
  - future CalDAV app passwords
- Add deliberate CORS and CSP policies.
- Avoid wildcard production CORS.
- Hide unnecessary server-identifying headers where possible.
- Add security headers deliberately and verify the UI.

Acceptance criteria:

- `GET /health` is explicitly anonymous.
- Admin UI requires an authenticated admin session.
- Home Assistant intake requires `intake:write`.
- Feed tokens cannot write.
- Client write tokens cannot access admin UI.
- Production CORS uses explicit origins.
- CSP supports Blazor WASM, BluQube, SignalR, and PWA assets.

## Observability

Adopt now:

- Use `ILogger<T>` with structured log properties.
- Keep logs diagnostic and operational.
- Keep audit entries as durable app state.
- Include useful context such as correlation ID, actor/client ID, event ID, decision ID, and request path where available.
- Use warning/error logs only when actionable.

Do not:

- use ordinary logs as the audit trail
- log raw credentials, raw feed tokens, passwords, or authorization headers
- log full AI provider payloads unless a deliberate redaction strategy exists

## Audit

Audit is required for:

- submitted intents
- review decisions
- approvals
- rejections
- edits
- deletes
- reschedules
- credential creation/rotation/revocation
- feed token creation/rotation/revocation
- relevant auth-sensitive events

Acceptance criteria:

- Every meaningful state mutation writes an audit entry.
- Audit entries are queryable by event/intent/decision where relevant.
- Audit entries do not contain raw secrets.

## Testing

Adopt now:

- Use red/green/refactor TDD for domain behaviour and risky application workflows.
- Unit tests for domain/review logic.
- Unit tests for validators.
- Integration tests for Marten persistence.
- Integration tests for auth policies and key HTTP/BluQube flows.
- Tests for ICS feed output once feeds exist.
- Tests for SignalR notification publishing at the service boundary.

### Red/Green/Refactor Workflow

Default implementation loop:

1. Write or update a failing test that describes the desired behaviour.
2. Run the focused test and confirm it fails for the expected reason.
3. Implement the smallest useful change to make the test pass.
4. Run the focused test and confirm it passes.
5. Refactor while keeping tests green.
6. Run the broader relevant test set before finishing the slice.

Use this especially for:

- calendar routing rules
- clash detection
- Child responsibility/chaperone rules
- delete and reschedule safety
- AI suggestion merging
- auth scope enforcement
- feed projection rules
- BluQube command/handler behaviour

Where TDD is less useful, such as early UI layout exploration, still add tests around the underlying behaviour before considering the slice complete.

Initial required test areas:

- birthday and anniversary routing
- personal/family routing
- Child responsibility inference
- ambiguity staging
- clash detection
- non-busy reference events
- exact delete behaviour
- reschedule duplicate avoidance
- AI provider disabled/no-op behaviour
- auth token scope enforcement

Acceptance criteria:

- Tests run from the command line.
- New domain behaviour starts with a failing test or an explicitly documented reason when that is impractical.
- Domain tests do not require PostgreSQL.
- Persistence tests use real Marten/PostgreSQL behaviour.
- Auth tests prove feed tokens cannot write and write tokens cannot administer.

## Frontend

Adopt now:

- Blazor WASM client uses BluQube runners for server request/response work.
- SignalR only pushes invalidation/change notifications.
- UI refreshes data through BluQube queries after SignalR messages.
- PWA offline mode only queues safe new event intents.
- Approve, reject, delete, and reschedule require current server state.

Acceptance criteria:

- SignalR hub methods do not mutate calendar state.
- Offline caches do not store raw credentials or feed tokens.
- Cached views are visibly stale/offline when disconnected.

## Intentionally Skip For V1

Skip until there is a clear product need:

- full ASP.NET Identity
- public registration
- password reset email flow
- OAuth/OIDC login
- multi-role family permissions beyond simple admin/client/feed scopes
- full Marten event sourcing
- complex recurrence editing
- full writable virtual CalDAV calendars

These are not rejected permanently. They are deferred to keep the first implementation focused.

## Review Checklist

Before merging implementation work, check:

- Does the change keep calendar policy on the server/domain side?
- Are warnings still treated as errors?
- Are package versions centralized?
- Are validation, authorization, and domain policy in the right layers?
- Does every state mutation write audit?
- Are logs structured and free of secrets?
- Are new endpoints protected by default?
- Are anonymous endpoints explicit?
- Are tests added at the right level for the risk?
- Does the WASM client avoid server-only dependencies?
- Does SignalR only notify, rather than mutate?
