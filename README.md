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
- [Continuous integration](docs/engineering/ci.md)
- [Public repo privacy](docs/engineering/public-repo-privacy.md)

## Local Configuration

The server requires `Database:ConnectionString` at startup. Keep real connection strings out of source control; set `Database__ConnectionString` in the local environment or use .NET user secrets for development.
