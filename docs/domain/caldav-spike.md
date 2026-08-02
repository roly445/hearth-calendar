# CalDAV Spike

This spike defines the smallest app-owned CalDAV surface for Hearth Calendar.

CalDAV remains an intake adapter. PostgreSQL through Marten is still the source of truth, and accepted CalDAV writes enter the same app-owned intake, review, and audit model as other sources.

## Goals

- Support app-password-style CalDAV credentials that are separate from admin, intake, and feed tokens.
- Prove a writable Smart Inbox calendar.
- Keep read-only virtual calendars projected from approved app state only.
- Avoid depending on Baikal or any external calendar server.

## Client Operations

The minimum operations to validate with OneCalendar and DAVx5 are:

| Operation | Path Shape | Purpose | Spike Status |
| --- | --- | --- | --- |
| `OPTIONS` | `/caldav/` | Discover DAV methods and auth challenge behaviour. | Implemented. |
| `PROPFIND` depth `0` | `/caldav/` | Discover principal or calendar-home hints. | Implemented. |
| `PROPFIND` depth `1` | `/caldav/calendars/` | Discover available calendars. | Implemented. |
| `PROPFIND` depth `0/1` | `/caldav/calendars/smart-inbox/` | Discover writable Smart Inbox metadata. | Implemented. |
| `PUT` | `/caldav/calendars/smart-inbox/{uid}.ics` | Submit a VEVENT into intake/review. | Implemented with idempotent metadata and review pipeline integration. |
| `GET` | `/caldav/calendars/{calendar}/{eventId}.ics` | Read one approved event from a read-only virtual calendar. | Implemented for virtual calendars. |
| `REPORT calendar-query` | `/caldav/calendars/{calendar}/` | Sync approved events for read-only calendars. | Implemented for virtual calendars with date-range filtering. |
| `DELETE` | `/caldav/calendars/smart-inbox/{uid}.ics` | Request a delete through safe mutation policy. | Later phase. |

## Calendar Model

| Calendar | Write | Read | Source |
| --- | --- | --- | --- |
| Smart Inbox | Yes | Eventually returns submitted objects and review state hints. | CalDAV intake. |
| Combined | No | Approved events only. | Virtual calendar projection. |
| Adult A | No | Approved events only. | Virtual calendar projection. |
| Adult B | No | Approved events only. | Virtual calendar projection. |
| Child | No | Approved events only. | Virtual calendar projection. |
| Family | No | Approved events only. | Virtual calendar projection. |
| Events | No | Approved events only. | Virtual calendar projection. |

The `Review` virtual calendar is not exported as an approved CalDAV calendar.

## Authentication

CalDAV uses HTTP Basic with an app password:

```text
Authorization: Basic base64(client-name:app-password)
```

Credentials produce:

- token kind: `caldav`
- optional read scope: `caldav:read`
- write scope: `caldav:write`
- readable calendar claims for approved virtual calendars
- writable calendar claim: `smart-inbox`

CalDAV credentials are intentionally separate from:

- admin cookie/session credentials
- intake bearer tokens
- read-only feed bearer/query tokens

Raw app passwords must never be stored or written to audit metadata. Configuration and future persisted credential documents store hashes only.

Read credentials can be scoped to approved virtual calendars independently from writable Smart Inbox credentials. Feed bearer/query tokens, intake bearer tokens, and admin cookie/session credentials cannot use the CalDAV protocol surface.

## Implemented Smart Inbox Write Flow

`PUT /caldav/calendars/smart-inbox/{itemId}.ics` accepts a minimal VEVENT with:

- `SUMMARY`
- `DTSTART`
- optional `DTEND`

The endpoint creates:

- `EventIntent.Source = CalDav`
- `EventIntent.SourceMode = Passive`
- `EventIntent.RawText = SUMMARY`
- `EventIntent.Payload` from `DTSTART` and optional `DTEND`
- an `IntakeIntentSubmitted` audit entry with CalDAV metadata
- a normal review decision through the deterministic and optional AI-assisted review pipeline
- a review audit entry for approved, staged, or rejected decisions

Supported date/time forms:

- `DTSTART:yyyyMMddTHHmmssZ`
- `DTEND:yyyyMMddTHHmmssZ`
- `DTSTART;VALUE=DATE:yyyyMMdd`

All-day values remain all-day in the app payload and do not fake midnight times.

Smart Inbox `PUT` persists CalDAV object metadata by `{calendarId}/{itemId}` without storing the raw ICS body.

Policy:

- First `PUT` creates a CalDAV object metadata row, an `EventIntent`, and an audit entry.
- First `PUT` also stores the review outcome in the same persistence boundary: approved writes become approved app events, ambiguous writes become staged review items, and rejected outcomes remain hidden from approved views.
- Repeated identical `PUT` returns the existing ETag and does not create a duplicate intent, audit entry, review decision, approved event, AI review call, or UI notification.
- Changed `PUT` to the same `{itemId}` updates the metadata row, records a new ETag, and creates a new replacement `EventIntent`, intake audit, review decision, and review audit.
- The current metadata row links to the latest intent for that URI.
- Raw app passwords, bearer tokens, and raw ICS content are not persisted in the CalDAV object metadata.
- Connected UI clients are notified only after the CalDAV metadata and review outcome persist successfully.
- AI review remains advisory and plug-and-play through `IAiReviewProvider`; deterministic safety decisions still win.

## Read-Only Virtual Calendar Projection

`GET /caldav/calendars/{calendar}/{eventId}.ics` returns a single approved event for the requested virtual calendar when the authenticated CalDAV credential has that calendar in its readable calendar claims. The `{eventId}` is the app-owned approved event id and maps to the existing feed UID shape.

`REPORT calendar-query` supports `C:time-range` with `yyyyMMddTHHmmssZ` bounds. The endpoint treats the CalDAV `end` bound as exclusive when converting to the app's `DateOnly` query. Missing or malformed ranges fall back to a broad compatibility window.

Both read paths reuse the same approved-event projection and ICS output rules as feeds:

- staged and rejected events are excluded
- birthday and anniversary reference events remain transparent/free
- all-day events are emitted as `VALUE=DATE` rather than midnight date-times
- read-only virtual calendars reject write attempts without exposing event details

## Next Implementation Slice

The next CalDAV work should be a compatibility spike against DAVx5 and OneCalendar, plus explicit follow-up issues for any client-specific sync expectations.

Full CalDAV recurrence, attendee handling, sync tokens, timezone components, and client-driven deletes/reschedules should remain explicit follow-up work.
