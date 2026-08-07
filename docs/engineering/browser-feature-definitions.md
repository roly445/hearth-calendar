# Browser Feature Definitions

Browser-level UI behaviour is defined in the root `features/` folder before it
is automated. These files are product-facing contracts for future browser tests,
not component implementation notes.

## Files

| File | Purpose |
| --- | --- |
| `features/calendar-workspace.feature` | Current admin workspace behaviours. |
| `features/offline-queue.feature` | PWA offline capture, retry, and cached data behaviours. |
| `features/live-updates.feature` | SignalR refresh and reconnect behaviours. |
| `features/auth-session.feature` | Admin session behaviours for browser automation. |

## Writing Rules

- Write scenarios in Given/When/Then language from the user's perspective.
- Use generic example event text only.
- Do not include real names, addresses, schools, clubs, workplaces, calendar URLs,
  tokens, internal hostnames, or private infrastructure details.
- Avoid CSS selectors, component class names, route internals, and generated asset names.
- Prefer stable user-visible language and behaviour.
- Keep setup details broad enough that Playwright or another runner can implement
  them without rewriting the feature files.

## Automation Notes

Future browser automation should treat these feature files as the source of
intent. Tests may use user-visible roles and labels directly, with explicit test
IDs only where the UI has no stable accessible handle.

Browser automation should cover the scenarios that are already implemented by
the app. Auth/session coverage now exercises the server-rendered login flow,
including the rule that unauthenticated users do not load the WASM app shell
before signing in.
