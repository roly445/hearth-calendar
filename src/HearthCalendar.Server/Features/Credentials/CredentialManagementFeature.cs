using System.Security.Claims;
using System.Security.Cryptography;
using BluQube.Authorization;
using BluQube.Commands;
using BluQube.Constants;
using BluQube.Queries;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Server.Features.Credentials;

public sealed class GetCredentialInventoryQueryProcessor(IHearthCalendarCredentialStore store)
    : IQueryProcessor<GetCredentialInventoryQuery, CredentialInventoryResult>
{
    public async ValueTask<QueryResult<CredentialInventoryResult>> Handle(
        GetCredentialInventoryQuery request,
        CancellationToken cancellationToken)
    {
        var inventory = await store.QueryInventoryAsync(cancellationToken);

        return QueryResult<CredentialInventoryResult>.Succeeded(new CredentialInventoryResult(
            inventory.ClientCredentials.Select(CredentialManagementMapping.ToDto).ToArray(),
            inventory.FeedTokens.Select(CredentialManagementMapping.ToDto).ToArray(),
            inventory.CalDavCredentials.Select(CredentialManagementMapping.ToDto).ToArray()));
    }
}

public sealed class CreateClientCredentialCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<CreateClientCredentialCommand, CreateClientCredentialResult>
{
    public async ValueTask<CommandResult<CreateClientCredentialResult>> Handle(
        CreateClientCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var name = CredentialManagementSupport.NormalizeName(request.Name);
        if (name is null)
        {
            return CredentialManagementSupport.InvalidSecretName<CreateClientCredentialResult>();
        }

        var secret = CredentialSecretGenerator.Generate("hc_client");
        var credential = new ClientCredentialDocument
        {
            Id = Guid.NewGuid(),
            ClientName = name,
            SecretHash = HearthCalendarSecretHasher.Hash(secret),
            Scopes = CredentialManagementSupport.NormalizeScopes(request.Scopes, [HearthCalendarAuth.IntakeWriteScope]),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.StoreClientCredentialAsync(
            credential,
            CredentialManagementAudits.ForClientCredential(AuditAction.ClientCredentialCreated, credential),
            cancellationToken);

        return CommandResult<CreateClientCredentialResult>.Succeeded(
            new CreateClientCredentialResult(credential.Id, credential.ClientName, secret, "Client credential created."));
    }
}

public sealed class CreateFeedTokenCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<CreateFeedTokenCommand, CreateFeedTokenResult>
{
    public async ValueTask<CommandResult<CreateFeedTokenResult>> Handle(
        CreateFeedTokenCommand request,
        CancellationToken cancellationToken)
    {
        var name = CredentialManagementSupport.NormalizeName(request.Name);
        var calendars = CredentialManagementSupport.NormalizeCalendars(request.AllowedCalendars);
        if (name is null || calendars.Count == 0)
        {
            return CommandResult<CreateFeedTokenResult>.Failed(
                new BluQubeErrorData("INVALID_FEED_TOKEN", "Feed tokens need a name and at least one calendar."));
        }

        var secret = CredentialSecretGenerator.Generate("hc_feed");
        var token = new FeedTokenDocument
        {
            Id = Guid.NewGuid(),
            Name = name,
            TokenHash = HearthCalendarSecretHasher.Hash(secret),
            AllowedCalendars = calendars,
            Scopes = [HearthCalendarAuth.FeedReadScope],
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.StoreFeedTokenAsync(
            token,
            CredentialManagementAudits.ForFeedToken(AuditAction.FeedTokenCreated, token),
            cancellationToken);

        return CommandResult<CreateFeedTokenResult>.Succeeded(
            new CreateFeedTokenResult(token.Id, token.Name, secret, "Feed token created."));
    }
}

public sealed class CreateCalDavCredentialCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<CreateCalDavCredentialCommand, CreateCalDavCredentialResult>
{
    public async ValueTask<CommandResult<CreateCalDavCredentialResult>> Handle(
        CreateCalDavCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var name = CredentialManagementSupport.NormalizeName(request.Name);
        var readable = CredentialManagementSupport.NormalizeCalDavCalendars(request.ReadableCalendars);
        var writable = CredentialManagementSupport.NormalizeCalDavCalendars(request.WritableCalendars);
        if (name is null || readable.Count == 0 && writable.Count == 0)
        {
            return CommandResult<CreateCalDavCredentialResult>.Failed(
                new BluQubeErrorData("INVALID_CALDAV_CREDENTIAL", "CalDAV credentials need a name and at least one calendar."));
        }

        var secret = CredentialSecretGenerator.Generate("hc_caldav");
        var scopes = writable.Count == 0
            ? [HearthCalendarAuth.CalDavReadScope]
            : new[] { HearthCalendarAuth.CalDavReadScope, HearthCalendarAuth.CalDavWriteScope };
        var credential = new CalDavCredentialDocument
        {
            Id = Guid.NewGuid(),
            Name = name,
            SecretHash = HearthCalendarSecretHasher.Hash(secret),
            ReadableCalendars = readable.Union(writable, StringComparer.OrdinalIgnoreCase).ToArray(),
            WritableCalendars = writable,
            Scopes = scopes,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await store.StoreCalDavCredentialAsync(
            credential,
            CredentialManagementAudits.ForCalDavCredential(AuditAction.CalDavCredentialCreated, credential),
            cancellationToken);

        return CommandResult<CreateCalDavCredentialResult>.Succeeded(
            new CreateCalDavCredentialResult(credential.Id, credential.Name, secret, "CalDAV credential created."));
    }
}

public sealed class RotateClientCredentialCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<RotateClientCredentialCommand, RotateClientCredentialResult>
{
    public async ValueTask<CommandResult<RotateClientCredentialResult>> Handle(
        RotateClientCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var credential = await store.LoadClientCredentialAsync(request.CredentialId, cancellationToken);
        if (credential is null)
        {
            return CredentialManagementSupport.NotFound<RotateClientCredentialResult>();
        }

        var secret = CredentialSecretGenerator.Generate("hc_client");
        var rotated = credential with { SecretHash = HearthCalendarSecretHasher.Hash(secret), LastUsedAt = null, RevokedAt = null };
        await store.StoreClientCredentialAsync(
            rotated,
            CredentialManagementAudits.ForClientCredential(AuditAction.ClientCredentialRotated, rotated),
            cancellationToken);

        return CommandResult<RotateClientCredentialResult>.Succeeded(
            new RotateClientCredentialResult(rotated.Id, rotated.ClientName, secret, "Client credential rotated."));
    }
}

public sealed class RotateFeedTokenCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<RotateFeedTokenCommand, RotateFeedTokenResult>
{
    public async ValueTask<CommandResult<RotateFeedTokenResult>> Handle(
        RotateFeedTokenCommand request,
        CancellationToken cancellationToken)
    {
        var token = await store.LoadFeedTokenAsync(request.TokenId, cancellationToken);
        if (token is null)
        {
            return CredentialManagementSupport.NotFound<RotateFeedTokenResult>();
        }

        var secret = CredentialSecretGenerator.Generate("hc_feed");
        var rotated = token with { TokenHash = HearthCalendarSecretHasher.Hash(secret), LastUsedAt = null, RevokedAt = null };
        await store.StoreFeedTokenAsync(
            rotated,
            CredentialManagementAudits.ForFeedToken(AuditAction.FeedTokenRotated, rotated),
            cancellationToken);

        return CommandResult<RotateFeedTokenResult>.Succeeded(
            new RotateFeedTokenResult(rotated.Id, rotated.Name, secret, "Feed token rotated."));
    }
}

public sealed class RotateCalDavCredentialCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<RotateCalDavCredentialCommand, RotateCalDavCredentialResult>
{
    public async ValueTask<CommandResult<RotateCalDavCredentialResult>> Handle(
        RotateCalDavCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var credential = await store.LoadCalDavCredentialAsync(request.CredentialId, cancellationToken);
        if (credential is null)
        {
            return CredentialManagementSupport.NotFound<RotateCalDavCredentialResult>();
        }

        var secret = CredentialSecretGenerator.Generate("hc_caldav");
        var rotated = credential with { SecretHash = HearthCalendarSecretHasher.Hash(secret), LastUsedAt = null, RevokedAt = null };
        await store.StoreCalDavCredentialAsync(
            rotated,
            CredentialManagementAudits.ForCalDavCredential(AuditAction.CalDavCredentialRotated, rotated),
            cancellationToken);

        return CommandResult<RotateCalDavCredentialResult>.Succeeded(
            new RotateCalDavCredentialResult(rotated.Id, rotated.Name, secret, "CalDAV credential rotated."));
    }
}

public sealed class RevokeClientCredentialCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<RevokeClientCredentialCommand, RevokeClientCredentialResult>
{
    public async ValueTask<CommandResult<RevokeClientCredentialResult>> Handle(
        RevokeClientCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var credential = await store.LoadClientCredentialAsync(request.CredentialId, cancellationToken);
        if (credential is null)
        {
            return CredentialManagementSupport.NotFound<RevokeClientCredentialResult>();
        }

        var revoked = credential with { RevokedAt = DateTimeOffset.UtcNow };
        await store.StoreClientCredentialAsync(
            revoked,
            CredentialManagementAudits.ForClientCredential(AuditAction.ClientCredentialRevoked, revoked),
            cancellationToken);

        return CommandResult<RevokeClientCredentialResult>.Succeeded(
            new RevokeClientCredentialResult(revoked.Id, revoked.ClientName, "Client credential revoked."));
    }
}

public sealed class RevokeFeedTokenCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<RevokeFeedTokenCommand, RevokeFeedTokenResult>
{
    public async ValueTask<CommandResult<RevokeFeedTokenResult>> Handle(
        RevokeFeedTokenCommand request,
        CancellationToken cancellationToken)
    {
        var token = await store.LoadFeedTokenAsync(request.TokenId, cancellationToken);
        if (token is null)
        {
            return CredentialManagementSupport.NotFound<RevokeFeedTokenResult>();
        }

        var revoked = token with { RevokedAt = DateTimeOffset.UtcNow };
        await store.StoreFeedTokenAsync(
            revoked,
            CredentialManagementAudits.ForFeedToken(AuditAction.FeedTokenRevoked, revoked),
            cancellationToken);

        return CommandResult<RevokeFeedTokenResult>.Succeeded(
            new RevokeFeedTokenResult(revoked.Id, revoked.Name, "Feed token revoked."));
    }
}

public sealed class RevokeCalDavCredentialCommandHandler(IHearthCalendarCredentialStore store)
    : ICommandHandler<RevokeCalDavCredentialCommand, RevokeCalDavCredentialResult>
{
    public async ValueTask<CommandResult<RevokeCalDavCredentialResult>> Handle(
        RevokeCalDavCredentialCommand request,
        CancellationToken cancellationToken)
    {
        var credential = await store.LoadCalDavCredentialAsync(request.CredentialId, cancellationToken);
        if (credential is null)
        {
            return CredentialManagementSupport.NotFound<RevokeCalDavCredentialResult>();
        }

        var revoked = credential with { RevokedAt = DateTimeOffset.UtcNow };
        await store.StoreCalDavCredentialAsync(
            revoked,
            CredentialManagementAudits.ForCalDavCredential(AuditAction.CalDavCredentialRevoked, revoked),
            cancellationToken);

        return CommandResult<RevokeCalDavCredentialResult>.Succeeded(
            new RevokeCalDavCredentialResult(revoked.Id, revoked.Name, "CalDAV credential revoked."));
    }
}

public static class CredentialSecretGenerator
{
    public static string Generate(string prefix)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);

        return $"{prefix}_{token}";
    }
}

public static class CredentialManagementSupport
{
    public static string? NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var name = value.Trim();

        return name.Length > 80 ? null : name;
    }

    public static IReadOnlyList<string> NormalizeScopes(
        IReadOnlyList<string>? scopes,
        IReadOnlyList<string> fallback)
    {
        var normalized = (scopes ?? [])
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalized.Length == 0 ? fallback : normalized;
    }

    public static IReadOnlyList<string> NormalizeCalendars(IReadOnlyList<string>? calendars) =>
        (calendars ?? [])
            .Select(ParseCalendar)
            .Where(calendar => calendar is not null && calendar != VirtualCalendar.Review)
            .Select(calendar => calendar!.Value.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> NormalizeCalDavCalendars(IReadOnlyList<string>? calendars) =>
        (calendars ?? [])
            .Where(calendar => !string.IsNullOrWhiteSpace(calendar))
            .Select(calendar => calendar.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static CommandResult<TResult> InvalidSecretName<TResult>()
        where TResult : ICommandResult =>
        CommandResult<TResult>.Failed(new BluQubeErrorData("INVALID_CREDENTIAL_NAME", "Credentials need a short name."));

    public static CommandResult<TResult> NotFound<TResult>()
        where TResult : ICommandResult =>
        CommandResult<TResult>.Failed(new BluQubeErrorData("CREDENTIAL_NOT_FOUND", "The credential was not found."));

    private static VirtualCalendar? ParseCalendar(string value) =>
        Enum.TryParse<VirtualCalendar>(value.Trim(), ignoreCase: true, out var calendar)
            ? calendar
            : null;
}

public static class CredentialManagementMapping
{
    public static ClientCredentialMetadataDto ToDto(ClientCredentialDocument credential) =>
        new(
            credential.Id,
            credential.ClientName,
            credential.Scopes,
            credential.CreatedAt,
            credential.LastUsedAt,
            credential.RevokedAt);

    public static FeedTokenMetadataDto ToDto(FeedTokenDocument token) =>
        new(
            token.Id,
            token.Name,
            token.AllowedCalendars,
            token.Scopes,
            token.CreatedAt,
            token.LastUsedAt,
            token.RevokedAt);

    public static CalDavCredentialMetadataDto ToDto(CalDavCredentialDocument credential) =>
        new(
            credential.Id,
            credential.Name,
            credential.ReadableCalendars,
            credential.WritableCalendars,
            credential.Scopes,
            credential.CreatedAt,
            credential.LastUsedAt,
            credential.RevokedAt);
}

public static class CredentialManagementAudits
{
    public static AuditEntry ForClientCredential(AuditAction action, ClientCredentialDocument credential) =>
        ForCredential(action, credential.Id, credential.ClientName, credential.Scopes, []);

    public static AuditEntry ForFeedToken(AuditAction action, FeedTokenDocument token) =>
        ForCredential(action, token.Id, token.Name, token.Scopes, token.AllowedCalendars);

    public static AuditEntry ForCalDavCredential(AuditAction action, CalDavCredentialDocument credential) =>
        ForCredential(action, credential.Id, credential.Name, credential.Scopes, credential.ReadableCalendars);

    private static AuditEntry ForCredential(
        AuditAction action,
        Guid id,
        string name,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> calendars) =>
        new(
            AuditEntryId.New(),
            action,
            ActorRef.System,
            DateTimeOffset.UtcNow,
            $"Credential action {action}.",
            Metadata: new Dictionary<string, string>
            {
                ["credentialId"] = id.ToString(),
                ["name"] = name,
                ["scopes"] = string.Join(",", scopes),
                ["calendars"] = string.Join(",", calendars)
            });
}

public sealed class CredentialInventoryQueryAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<GetCredentialInventoryQuery>(accessor);

public sealed class CreateClientCredentialCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<CreateClientCredentialCommand>(accessor);

public sealed class CreateFeedTokenCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<CreateFeedTokenCommand>(accessor);

public sealed class CreateCalDavCredentialCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<CreateCalDavCredentialCommand>(accessor);

public sealed class RotateClientCredentialCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<RotateClientCredentialCommand>(accessor);

public sealed class RotateFeedTokenCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<RotateFeedTokenCommand>(accessor);

public sealed class RotateCalDavCredentialCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<RotateCalDavCredentialCommand>(accessor);

public sealed class RevokeClientCredentialCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<RevokeClientCredentialCommand>(accessor);

public sealed class RevokeFeedTokenCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<RevokeFeedTokenCommand>(accessor);

public sealed class RevokeCalDavCredentialCommandAuthorizer(IHttpContextAccessor accessor)
    : AdminBluQubeAuthorizer<RevokeCalDavCredentialCommand>(accessor);
