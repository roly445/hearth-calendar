# hearth-calendar

Hearth Calendar is a .NET family calendar app that owns calendar policy and stores app state in PostgreSQL through Marten.

## Planning Docs

- [Product boundaries and roadmap](docs/domain/product-boundaries-and-roadmap.md)
- [Event model](docs/domain/event-model.md)
- [Domain model](docs/domain/domain-model.md)
- [Auth stack](docs/domain/auth-stack.md)
- [UI stack](docs/domain/ui-stack.md)
- [GitHub issue backlog](docs/planning/github-issues.md)

## Engineering Standards

- [.NET app defaults compliance](docs/engineering/dotnet-app-defaults-compliance.md)
- [BluQube usage guide](docs/engineering/bluqube-usage.md)
- [Browser testing](docs/engineering/browser-testing.md)
- [Continuous integration](docs/engineering/ci.md)
- [Deployment and runtime configuration](docs/engineering/deployment-runtime.md)
- [Public repo privacy](docs/engineering/public-repo-privacy.md)

## Local Configuration

The server requires `Database:ConnectionString` at startup. Keep real connection strings out of source control; set `Database__ConnectionString` in the local environment or use .NET user secrets for development.

## Local Checks

Run the fast checks without Docker:

```bash
npm run test:assets
npm run test:browser
npm run test:dotnet:fast
dotnet build HearthCalendar.slnx
```

Run the Marten/PostgreSQL integration tests when Docker is available:

```bash
npm run test:dotnet:docker
```

`npm run test:dotnet:fast` excludes tests tagged `Category=Docker`. `npm run test:dotnet:docker` runs the Docker-backed tests only.
