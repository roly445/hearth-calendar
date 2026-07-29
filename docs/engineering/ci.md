# Continuous Integration

Hearth Calendar should have CI from the first implementation PR.

The initial workflow belongs to Phase 0, alongside the .NET solution scaffold, so the first workflow has a real solution to restore, build, and test.

## Initial Workflow

Create:

```text
.github/workflows/ci.yml
```

Triggers:

- pull requests targeting `main`
- pushes to `main`

Initial jobs:

- checkout
- set up .NET SDK
- restore
- build with warnings as errors
- run tests

Recommended shape:

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test --configuration Release --no-build
```

If the project uses a different SDK version when scaffolded, update `dotnet-version` at the same time as the project files.

## Expansion Points

Add these as the app grows:

| Phase | CI Addition |
| --- | --- |
| Persistence | PostgreSQL-backed integration tests, likely via Testcontainers or a service container. |
| Auth | Authorization policy/API integration tests. |
| UI | Blazor WASM build verification; browser/component tests if introduced. |
| Feeds | ICS parser and snapshot tests. |
| CalDAV | Protocol compatibility tests or fixtures where practical. |

## Branch Protection

After the first CI workflow is merged and has run successfully:

- require the CI check before merging to `main`
- require PRs for changes to `main`
- consider requiring up-to-date branches before merge
- consider blocking force pushes to `main`

Branch protection should be configured after the actual check name is visible in GitHub.

## Acceptance Criteria

- CI runs on pull requests targeting `main`.
- CI runs on pushes to `main`.
- CI restores, builds, and tests the solution.
- CI fails on compiler warnings through project settings.
- CI does not require real secrets for normal build/test.
- Branch protection is configured after the CI check exists.
