# CalDAV Spike

This spike defines the smallest app-owned CalDAV surface for Hearth Calendar.

CalDAV remains an intake adapter. PostgreSQL through Marten is still the source of truth, and accepted CalDAV writes enter the same app-owned intake and audit model as other sources. Full review, approval, staging, and rejection processing is the next implementation step after the protocol write shape is proven.

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
| `PUT` | `/caldav/calendars/smart-inbox/{uid}.ics` | Submit a VEVENT into intake/review. | Implemented with idempotent metadata. |
| `GET` | `/caldav/calendars/smart-inbox/{uid}.ics` | Let clients confirm a stored object. | Planned next. |
| `REPORT calendar-query` | `/caldav/calendars/{calendar}/` | Sync approved events for read-only calendars. | Planned next. |
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
- write scope: `caldav:write`
- writable calendar claim: `smart-inbox`

CalDAV credentials are intentionally separate from:

- admin cookie/session credentials
- intake bearer tokens
- read-only feed bearer/query tokens

Raw app passwords must never be stored or written to audit metadata. Configuration and future persisted credential documents store hashes only.

## Implemented Prototype

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

Supported date/time forms:

- `DTSTART:yyyyMMddTHHmmssZ`
- `DTEND:yyyyMMddTHHmmssZ`
- `DTSTART;VALUE=DATE:yyyyMMdd`

All-day values remain all-day in the app payload and do not fake midnight times.

Smart Inbox `PUT` persists CalDAV object metadata by `{calendarId}/{itemId}` without storing the raw ICS body.

Policy:

- First `PUT` creates a CalDAV object metadata row, an `EventIntent`, and an audit entry.
- Repeated identical `PUT` returns the existing ETag and does not create a duplicate intent or audit entry.
- Changed `PUT` to the same `{itemId}` updates the metadata row, records a new ETag, and creates a new replacement `EventIntent` and audit entry.
- The current metadata row links to the latest intent for that URI.
- Raw app passwords, bearer tokens, and raw ICS content are not persisted in the CalDAV object metadata.

## Next Implementation Slice

The next CalDAV PR should add enough read/query behaviour for real clients:

1. `GET` for Smart Inbox objects accepted by the current process or persisted as CalDAV object metadata.
2. `REPORT calendar-query` for read-only virtual calendars backed by approved-event queries.
3. Compatibility notes from manual DAVx5 and OneCalendar setup attempts.

Full CalDAV recurrence, attendee handling, sync tokens, timezone components, and client-driven deletes/reschedules should remain explicit follow-up work.
