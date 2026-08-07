# Deployment And Runtime Configuration

This document describes the first deployment model for Hearth Calendar. It is intentionally generic so the public repository does not reveal private hostnames, tokens, credentials, or household details.

## Runtime Shape

The deployed app is a hosted Blazor WebAssembly application served by `HearthCalendar.Server`.

```text
Browser/PWA
  -> ASP.NET Core server
       -> Blazor WASM static assets
       -> BluQube command/query endpoints
       -> SignalR calendar update hub
       -> Home Assistant intake endpoint
       -> ICS feed endpoints
       -> CalDAV endpoints
       -> Marten/PostgreSQL
```

PostgreSQL is the source of truth. The browser may cache the app shell and selected read models for PWA/offline use, but approved calendar state, review decisions, credentials, and audits are owned by the server and persisted through Marten.

## Required Configuration

Use environment variables, a deployment secret store, or .NET user secrets for local development. Do not commit real values to `appsettings*.json`.

| Key | Required | Example | Notes |
| --- | --- | --- | --- |
| `Database:ConnectionString` | Yes | `Host=db.example;Port=5432;Database=hearth_calendar;Username=app_user;Password=<secret>;SSL Mode=Require` | Required at startup and validated with `ValidateOnStart`. Use `Database__ConnectionString` as an environment variable. |
| `Database:SchemaName` | No | `hearth_calendar` | Defaults to `hearth_calendar`. Keep this stable after deployment unless a migration plan exists. |
| `Auth:AdminUsers` | Yes for admin UI access | See below | Admin users are configured with password hashes, not raw passwords. |
| `Security:Cors:AllowedOrigins` | Required when the browser origin differs from the server origin | `https://calendar.example.invalid` | Leave empty for same-origin deployments. Never use wildcard origins with credentials. |

### Environment Variable Mapping

.NET configuration uses double underscores for nested keys:

```text
Database__ConnectionString=Host=db.example;Port=5432;Database=hearth_calendar;Username=app_user;Password=<secret>;SSL Mode=Require
Database__SchemaName=hearth_calendar
Security__Cors__AllowedOrigins__0=https://calendar.example.invalid
```

Array indexes are zero-based. Add `__1`, `__2`, and so on for additional values.

## First Admin Bootstrap

The first admin user is provisioned out-of-band through configuration. The app does not ship with a default admin account and does not expose public registration.

Generate the password hash locally and pass only the hash to the running app:

```powershell
$password = Read-Host -Prompt "Admin password" -MaskInput
$password | dotnet run --no-launch-profile --project src/HearthCalendar.Server -- admin-password-hash --password-stdin
Remove-Variable password
```

Use the generated value as the first admin's password hash:

```text
Auth__AdminUsers__0__Username=admin-user
Auth__AdminUsers__0__DisplayName=Calendar Admin
Auth__AdminUsers__0__PasswordHash=pbkdf2-sha256:<iterations>:<salt>:<hash>
Auth__AdminUsers__0__Scopes__0=admin:web
```

For local development, prefer .NET user secrets or process-level environment variables. For deployment, use the host's secret store or environment-variable management. Do not commit the generated hash if it belongs to a real deployment, and never commit or log the raw password.

## Optional Bootstrap Credentials

The app supports bootstrap credentials in configuration and runtime-managed credentials in PostgreSQL. Prefer runtime-managed client, feed, and CalDAV credentials after the admin UI is available. Keep bootstrap configuration small and rotate away from it when practical.

### Admin Users

Admin users are configured under `Auth:AdminUsers`. Passwords must be stored as PBKDF2 hashes produced by the app's admin password hashing command, not as raw passwords.

```json
{
  "Auth": {
    "AdminUsers": [
      {
        "Username": "admin-user",
        "DisplayName": "Calendar Admin",
        "PasswordHash": "pbkdf2-sha256:<iterations>:<salt>:<hash>",
        "Scopes": [ "admin:web" ]
      }
    ]
  }
}
```

### Intake Client Tokens

Client tokens allow external systems to submit event intents. Store only `sha256:` hashes.

```json
{
  "Auth": {
    "ClientTokens": [
      {
        "Name": "external-intake",
        "SecretHash": "sha256:<base64-hash>",
        "Scopes": [ "intake:write" ]
      }
    ]
  }
}
```

### Feed Tokens

Feed tokens allow read-only ICS access for specific virtual calendars. Store only token hashes and use generic calendar scopes.

```json
{
  "Auth": {
    "FeedTokens": [
      {
        "Name": "adult-a-feed",
        "TokenHash": "sha256:<base64-hash>",
        "AllowedCalendars": [ "AdultA" ],
        "Scopes": [ "feed:read" ]
      }
    ]
  }
}
```

### CalDAV Credentials

CalDAV credentials use HTTP Basic authentication. Store only secret hashes. Writable calendars should be limited; the first write target is `smart-inbox`.

```json
{
  "Auth": {
    "CalDavCredentials": [
      {
        "Name": "caldav-client",
        "SecretHash": "sha256:<base64-hash>",
        "ReadableCalendars": [ "adult-a", "combined" ],
        "WritableCalendars": [ "smart-inbox" ],
        "Scopes": [ "caldav:read", "caldav:write" ]
      }
    ]
  }
}
```

## PostgreSQL And Marten

`Database:ConnectionString` is required. The connection string should come from the hosting platform secret store or process environment, not from committed config.

The current Marten setup uses:

```text
AutoCreateSchemaObjects = CreateOrUpdate
DatabaseSchemaName = Database:SchemaName
```

That is convenient for the first deployment because the app can create and update its document tables. Before a stricter production rollout, decide whether schema changes should move to an explicit migration/deployment step.

The database user should be scoped to the application database. Avoid using a superuser account. If the deployment keeps `CreateOrUpdate`, the app user needs enough DDL rights for the configured schema.

## CORS

CORS is only needed when the browser app is served from a different origin than the API host.

Recommended production shape:

```text
Security__Cors__AllowedOrigins__0=https://calendar.example.invalid
```

Rules:

- Use exact origins, including scheme and host.
- Do not use `*` because the app allows credentials when origins are configured.
- Do not copy local development origins into production.
- Leave the list empty for same-origin deployment; the server will deny cross-origin browser requests.

## Content Security Policy

The server emits an enforced CSP and related browser security headers for every response.

Current CSP assumptions:

```text
default-src 'self'
base-uri 'self'
object-src 'none'
frame-ancestors 'none'
img-src 'self' data: blob:
font-src 'self'
style-src 'self'
script-src 'self' 'wasm-unsafe-eval'
connect-src 'self'
manifest-src 'self'
worker-src 'self'
form-action 'self'
```

This supports same-origin Blazor WASM, BluQube HTTP calls, SignalR reconnects, the PWA manifest, and the service worker. If any future deployment splits static assets, APIs, or SignalR onto separate origins, update both CSP `connect-src` and CORS deliberately and verify the browser flows.

The server also emits:

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- a restrictive `Permissions-Policy`

## PWA And Static Assets

Generated `wwwroot` output is not committed. Build assets before or during the .NET build:

```bash
npm ci
npm run build:assets
dotnet build HearthCalendar.slnx --configuration Release
```

`HearthCalendar.Client` runs `npm run build:assets` before Blazor static web assets are resolved, so CI and deployment builds must install Node dependencies first. The service worker and manifest are generated from authored TypeScript, SCSS, and PWA source assets.

Offline browser caches must not contain raw credentials, feed tokens, admin passwords, or private provider payloads.

## Health Checks

The server exposes:

```text
GET /health
```

The endpoint is explicitly anonymous and returns a simple healthy response when the process is running. It does not prove database connectivity yet, so use it as a process smoke check rather than a full dependency check.

For deployment platforms that support health checks, start with `/health` and add deeper dependency checks in a later issue if the host needs readiness/liveness separation.

## Reverse Proxy And TLS Notes

Use HTTPS for any deployment that carries admin cookies, bearer tokens, feed tokens, or CalDAV credentials.

The app disables the Kestrel `Server` header. A reverse proxy may still add its own server-identifying headers; configure that at the proxy layer if needed.

Current cookie security uses `CookieSecurePolicy.SameAsRequest`. That means the app must see requests as HTTPS for cookies to be marked secure. For a first deployment, prefer one of these:

- terminate TLS at Kestrel, or
- proxy to the app over HTTPS, or
- add and verify forwarded-header handling before relying on TLS termination at a reverse proxy.

When running behind a proxy, also ensure:

- WebSocket or long-polling traffic for `/hubs/calendar-updates` is allowed.
- request bodies for CalDAV `PUT` are within the proxy's configured size limits.
- `/feeds/*`, `/caldav/*`, `/api/*`, `/hubs/*`, and Blazor static assets route to the same server unless CORS/CSP has been intentionally updated.

## Local Smoke Test

After deploying with generic, non-secret examples replaced by real secret-store values:

1. Open `/health` and confirm it returns healthy.
2. Load the root app URL and confirm Blazor starts without CSP errors in the browser console.
3. Sign in with an admin test account and confirm `/api/admin/session` returns an authenticated session.
4. Submit a generic event intent from the web UI and confirm it appears as approved, staged, or rejected according to review rules.
5. Open a second browser session and confirm SignalR updates refresh the visible calendar or review queue after a state change.
6. Request a configured ICS feed with a feed token and confirm an `.ics` response is returned.
7. Use a configured CalDAV read credential to discover calendars.
8. Turn the network offline in the browser dev tools and confirm the PWA app shell still opens.

Keep smoke-test data generic, for example `Adult A dentist` or `Family planning`, and remove any temporary credentials after testing.
