# Browser Testing

Hearth Calendar uses Playwright for browser-level UI automation.

The first browser suite is intentionally small: it proves that the hosted ASP.NET Core app can be started, reached by a real browser, and checked without real private data. Feature-level Blazor UI coverage is split into follow-up issues.

## Local Setup

Install Node dependencies and the Chromium browser used by the suite:

```bash
npm ci
npm run test:browser:install
```

Run the browser suite:

```bash
npm run test:browser
```

Playwright starts `HearthCalendar.Server` through `playwright.config.ts`. The app is given a generic, non-secret database connection string so startup configuration validation passes.

The browser configuration also sets `BrowserTests__UseSeedData=true` while running the server in the `Test` environment. That switch enables a narrow in-memory store and test admin principal for browser smoke coverage. It is only active when both the environment and config flag are set, and it must not be enabled in production deployments.

## Configuration

Useful environment variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `HEARTH_CALENDAR_BROWSER_PORT` | `5179` | Port used by the Playwright-started server. |
| `HEARTH_CALENDAR_BROWSER_BASE_URL` | `http://127.0.0.1:5179` | Full base URL. Use this to target an already-running local server. |

When targeting an existing server, make sure it uses public-repo-safe test data and does not expose real credentials, tokens, private hostnames, or personal calendar details.

## Test Conventions

- Put browser tests under `tests/HearthCalendar.BrowserTests`.
- Prefer user-visible roles, labels, and text.
- Add explicit test IDs only when accessible selectors are not stable enough.
- Keep test data generic, for example `Adult A`, `Adult B`, `Child`, `Family planning`, and `Adult A dentist`.
- Do not commit raw admin passwords, bearer tokens, feed tokens, CalDAV secrets, local hostnames, or private calendar names.
- Keep generated `wwwroot` output out of Git. The server build generates client assets as part of the normal build.

## Follow-Up Coverage

The infrastructure issue is #41. Fuller browser coverage belongs to:

- #48 calendar workspace smoke coverage
- #49 submit event intent flow
- #50 offline queue and reconnect sync
- #51 SignalR live update refresh
- #52 admin auth and session behaviour
