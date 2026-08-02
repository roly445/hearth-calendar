# BluQube Usage Guide

Hearth Calendar uses BluQube for typed command/query communication between the Blazor WebAssembly client and the ASP.NET Core server.

This document captures the repo-owned BluQube rules so a developer can work on the project without relying on local Codex skills.

## Role In The Architecture

BluQube is used for web UI request/response communication.

Use BluQube for:

- web UI commands that mutate app state
- web UI queries that load screen data
- validation-aware command handling
- authorization-aware command/query handling
- generated client requesters and server responders

Do not use BluQube for:

- SignalR notifications
- Home Assistant's external HTTP API contract
- ICS feed endpoints
- low-level CalDAV protocol endpoints
- background jobs

SignalR tells the UI that state changed. BluQube reloads the state.

## Project Boundaries

Expected project split:

```text
src/
  HearthCalendar.Client/
    Contracts/
      Ui/
    Features/
      Review/
      Events/
      Auth/

  HearthCalendar.Server/
    Domain/
    Features/
      Review/
      Events/
      Auth/
      Feeds/
    Infrastructure/
```

Rules:

- Client project contains command records, query records, result records, serializable DTOs, Blazor components, UI state, generated requesters, and calls through `ICommandRunner` / `IQueryRunner`.
- Server project contains the domain model, handlers, processors, validators, authorizers, Marten access, ASP.NET dependencies, and domain services.
- Do not put Marten, ASP.NET middleware, handlers, processors, or validators in the WASM client.
- Do not make client-owned BluQube contracts depend on server domain types. Use simple DTO values at the transport boundary and map them on the server.
- Do not add MediatR or another mediator package for UI command/query flow.

## Server Setup

The server entry point must be marked for BluQube responder generation:

```csharp
[BluQubeResponder]
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddValidatorsFromAssemblyContaining<Program>();
        builder.Services.AddBluQube(typeof(Program).Assembly);
        builder.Services.AddBluQubeAuthorization(typeof(Program).Assembly, options =>
        {
            options.RequireAuthorizationByDefault = true;
        });
        builder.Services.AddScoped<ICommandRunner, CommandRunner>();
        builder.Services.AddScoped<IQueryRunner, QueryRunner>();

        builder.Services.Configure<JsonOptions>(options =>
        {
            options.AddBluQubeJsonConverters();
        });

        var app = builder.Build();
        app.AddBluQubeApi();
        app.Run();
    }
}
```

Implementation notes:

- Register validators before command handlers need them.
- Register BluQube authorization with require-by-default.
- Call `app.AddBluQubeApi()` before `app.Run()`.
- Use the assembly that contains handlers, processors, validators, and authorizers.

## Client Setup

The Blazor WASM entry point must be marked for BluQube requester generation:

```csharp
[BluQubeRequester]
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        builder.Services.AddScoped<ICommandRunner, CommandRunner>();
        builder.Services.AddScoped<IQueryRunner, QueryRunner>();

        builder.Services.AddHttpClient(
            "bluqube",
            client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));

        builder.Services.AddTransient<CommandResultConverter>();
        builder.Services.AddBluQubeRequesters();

        await builder.Build().RunAsync();
    }
}
```

Implementation notes:

- The generated requesters use the named `bluqube` `HttpClient`.
- Components should call `ICommandRunner.Send(...)` and `IQueryRunner.Send(...)`.
- Components must check result status before reading result data.

## Commands

Commands modify state.

Command records live in `HearthCalendar.Client`.

```csharp
[BluQubeCommand(Path = "commands/review/approve")]
public sealed record ApproveReviewItemCommand(Guid ReviewDecisionId)
    : ICommand<ApproveReviewItemResult>;
```

Handlers live on the server.

```csharp
public sealed class ApproveReviewItemCommandHandler(
    ReviewWorkflow workflow,
    IEnumerable<IValidator<ApproveReviewItemCommand>> validators,
    ILogger<ApproveReviewItemCommandHandler> logger)
    : CommandHandler<ApproveReviewItemCommand, ApproveReviewItemResult>(validators, logger)
{
    protected override async Task<CommandResult<ApproveReviewItemResult>> HandleInternal(
        ApproveReviewItemCommand request,
        CancellationToken cancellationToken)
    {
        var result = await workflow.ApproveAsync(
            request.ReviewDecisionId,
            cancellationToken);

        return CommandResult<ApproveReviewItemResult>.Succeeded(result);
    }
}
```

Rules:

- New command behaviour should normally start with a failing handler/workflow test.
- Commands should return enough information for the UI to update or show a useful message.
- Commands that mutate calendar state must write audit entries.
- Commands should not publish SignalR messages until persistence succeeds.
- Unsafe domain operations return staged/rejected results rather than forcing success.

## Queries

Queries read state.

Query records live in `HearthCalendar.Client`.

```csharp
[BluQubeQuery(Path = "queries/review/queue", Method = "GET")]
public sealed record GetReviewQueueQuery : IQuery<ReviewQueueResult>;
```

Processors live on the server.

```csharp
public sealed class GetReviewQueueQueryProcessor(ReviewQueueReader reader)
    : IQueryProcessor<GetReviewQueueQuery, ReviewQueueResult>
{
    public async Task<QueryResult<ReviewQueueResult>> Handle(
        GetReviewQueueQuery request,
        CancellationToken cancellationToken)
    {
        var result = await reader.GetAsync(cancellationToken);

        return result.Items.Count == 0
            ? QueryResult<ReviewQueueResult>.Empty()
            : QueryResult<ReviewQueueResult>.Succeeded(result);
    }
}
```

Rules:

- New query behaviour should normally start with a failing processor/reader test.
- Return `Succeeded` when data exists.
- Return `Empty` for empty collection results.
- Return `NotFound` for missing single resources.
- Return `Unauthorized` for authorization failures.
- Return `Failed` only for genuine execution failures.

## Validation

Commands use FluentValidation through BluQube command handlers.

```csharp
public sealed class ApproveReviewItemCommandValidator
    : AbstractValidator<ApproveReviewItemCommand>
{
    public ApproveReviewItemCommandValidator()
    {
        RuleFor(x => x.ReviewDecisionId).NotEmpty();
    }
}
```

Rules:

- Put validators close to their feature.
- Validate transport/input shape in validators.
- Keep domain safety in domain services.
- Query validation happens inside processors where needed.

## Authorization

BluQube authorization is required by default.

Create one authorizer per protected request type:

```csharp
public sealed class ApproveReviewItemCommandAuthorizer(IHttpContextAccessor accessor)
    : IBluQubeAuthorizer<ApproveReviewItemCommand>
{
    public Task<AuthorizationResult> Authorize(
        ApproveReviewItemCommand request,
        CancellationToken cancellationToken)
    {
        var user = accessor.HttpContext?.User;
        var allowed = user?.HasClaim("scope", "admin:web") == true;

        return Task.FromResult(
            allowed
                ? AuthorizationResult.Succeed()
                : AuthorizationResult.Fail("Admin access is required."));
    }
}
```

Rules:

- Admin UI commands and queries require `admin:web`.
- Credential-management commands require `credentials:manage`.
- Anonymous BluQube requests must explicitly implement `IAllowAnonymousBluQubeRequest`.
- Unauthorized commands/queries should return BluQube unauthorized results, not throw generic failures.

## Component Calling Pattern

Components must check result status.

```razor
@inject ICommandRunner CommandRunner
@inject IQueryRunner QueryRunner

@code {
    private async Task Approve(Guid reviewDecisionId)
    {
        var result = await CommandRunner.Send(
            new ApproveReviewItemCommand(reviewDecisionId));

        if (result.IsSucceeded)
        {
            // Update local UI state or refresh a query.
            return;
        }

        if (result.Status == CommandResultStatus.Invalid)
        {
            // Render validation failures.
            return;
        }

        if (result.Status == CommandResultStatus.Unauthorized)
        {
            // Show an authorization message or redirect to login.
        }
    }
}
```

Do not read `Data`, `ValidationResult`, or `ErrorData` until the corresponding status has been checked.

## SignalR Interaction

After a successful command:

1. server persists state in Marten
2. server writes audit entries
3. server publishes a SignalR notification
4. client receives notification
5. client refreshes current screen data through a BluQube query

SignalR hub methods must not mutate calendar state.

## Common Troubleshooting

Generated endpoint returns 404:

- confirm `[BluQubeResponder]` is on the server entry point
- confirm command/query records have `[BluQubeCommand]` or `[BluQubeQuery]`
- confirm handler/processor exists on the server
- confirm `app.AddBluQubeApi()` runs

Client cannot send request:

- confirm `[BluQubeRequester]` is on the client entry point
- confirm named `bluqube` `HttpClient` is registered
- confirm `AddBluQubeRequesters()` is registered
- clean and rebuild if generated code is stale

Handler or processor not resolved:

- confirm server `AddBluQube(...)` scans the assembly containing handlers/processors
- confirm required dependencies are registered
- run with dependency injection validation enabled

Validation does not run:

- confirm validator is registered
- confirm handler inherits from `CommandHandler<T>` or `CommandHandler<T, TResult>`
- confirm validators are passed to the base constructor

Authorization returns unexpected failure:

- confirm `AddBluQubeAuthorization(...)` is registered
- confirm an authorizer exists for the request
- confirm the request is intentionally anonymous only when it implements `IAllowAnonymousBluQubeRequest`
- confirm the caller has the required scope claim

JSON serialization errors:

- confirm `options.AddBluQubeJsonConverters()` is configured on the server
- keep client-owned DTOs simple and serializable
- avoid leaking domain-only types that are awkward for WASM serialization

## Acceptance Criteria

- Commands, queries, result records, and UI DTOs live in the client project and are visible to the server for handlers/processors.
- Client-owned BluQube contracts do not reference server domain types.
- New command/query behaviour follows red/green/refactor unless explicitly impractical.
- Handlers, processors, validators, and authorizers live on the server.
- Client components use `ICommandRunner` and `IQueryRunner`.
- Result status is checked before result data is read.
- BluQube authorization is require-by-default.
- Anonymous BluQube requests are explicit.
- Mutating commands write audit entries.
- SignalR notifications are published only after successful persistence.
- SignalR hub methods do not mutate calendar state.
- External integrations keep explicit HTTP/ICS/CalDAV contracts.
