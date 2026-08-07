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
- set up Node.js
- restore client asset dependencies
- run client asset tests
- restore with NuGet parallelism disabled, which avoids socket exhaustion in constrained local and CI environments
- build with warnings as errors
- run fast .NET tests that do not require Docker
- run Docker-backed Marten/PostgreSQL tests in a separate named job

Recommended shape:

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:
    branches: [main]

jobs:
  fast-checks:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - uses: actions/setup-node@v4
        with:
          node-version: '24'
          cache: 'npm'

      - name: Restore client assets
        run: npm ci

      - name: Test client assets
        run: npm run test:assets

      - name: Restore
        run: dotnet restore --disable-parallel

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test fast .NET suite
        run: dotnet test --configuration Release --no-build --filter "Category!=Docker"
```

If the project uses a different SDK version when scaffolded, update `dotnet-version` at the same time as the project files.

## Test Suites

Use explicit commands so local and CI signals stay clear:

| Suite | Command | Docker Required |
| --- | --- | --- |
| Client assets | `npm run test:assets` | No |
| Browser smoke | `npm run test:browser` | No |
| Fast .NET tests | `npm run test:dotnet:fast` | No |
| Docker-backed .NET tests | `npm run test:dotnet:docker` | Yes |
| Build | `dotnet build HearthCalendar.slnx` | No |

Docker-backed tests must be tagged with:

```csharp
[Trait("Category", "Docker")]
```

The fast .NET suite excludes those tests with `Category!=Docker`. The Docker-backed suite runs only those tests with `Category=Docker`.

## Expansion Points

Add these as the app grows:

| Phase | CI Addition |
| --- | --- |
| Persistence | PostgreSQL-backed integration tests via Testcontainers in the Docker integration job. |
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
- CI restores, builds, and runs the fast .NET suite.
- CI runs Docker-backed Marten/PostgreSQL tests in a separately named job.
- CI fails on compiler warnings through project settings.
- CI does not require real secrets for normal build/test.
- Branch protection is configured after the CI check exists.
