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

The server requires `Database:ConnectionString` at startup. Keep real connection strings out of source control; set `Database__ConnectionString` in the local environment or create an ignored local settings file for development.

For local HTTPS development, use the app-specific `.dev.localhost` host:

```powershell
dotnet dev-certs https --trust
docker compose up -d postgres
Copy-Item .\src\HearthCalendar.Server\appsettings.Local.example.json .\src\HearthCalendar.Server\appsettings.Local.json
dotnet run --project .\src\HearthCalendar.Server\HearthCalendar.Server.csproj --launch-profile https
```

The compose file starts PostgreSQL on `localhost:5432` with database `hearth_calendar_dev`, username `postgres`, and password `postgres`. Edit `src/HearthCalendar.Server/appsettings.Local.json` with your local bootstrap admin password before signing in; ASP.NET Core Identity stores it as a BCrypt hash in PostgreSQL. The file is ignored by Git. The HTTPS profile serves `https://hearth-calendar.dev.localhost:7129`. Add the host to the Windows hosts file if it does not resolve locally.

## VS Code Debugging

The repo includes shared VS Code tasks and a launch profile. First run:

```powershell
dotnet restore .\HearthCalendar.slnx --disable-parallel
docker compose up -d postgres
npm install
```

Then open the Run and Debug panel and start `Hearth Calendar Server (HTTPS)`. The launch profile builds client assets, builds the solution, starts the server on `https://hearth-calendar.dev.localhost:7129`, and opens the browser when Kestrel is ready.

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
