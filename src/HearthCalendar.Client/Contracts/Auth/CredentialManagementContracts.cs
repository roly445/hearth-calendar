using BluQube.Attributes;
using BluQube.Commands;
using BluQube.Queries;

namespace HearthCalendar.Client.Contracts.Auth;

[BluQubeQuery(Path = "queries/admin/credentials")]
public sealed record GetCredentialInventoryQuery : IQuery<CredentialInventoryResult>;

public sealed record CredentialInventoryResult(
    IReadOnlyList<ClientCredentialMetadataDto> ClientCredentials,
    IReadOnlyList<FeedTokenMetadataDto> FeedTokens,
    IReadOnlyList<CalDavCredentialMetadataDto> CalDavCredentials) : IQueryResult;

[BluQubeCommand(Path = "commands/admin/credentials/client/create")]
public sealed record CreateClientCredentialCommand(
    string Name,
    IReadOnlyList<string> Scopes) : ICommand<CreateClientCredentialResult>;

[BluQubeCommand(Path = "commands/admin/credentials/feed/create")]
public sealed record CreateFeedTokenCommand(
    string Name,
    IReadOnlyList<string> AllowedCalendars) : ICommand<CreateFeedTokenResult>;

[BluQubeCommand(Path = "commands/admin/credentials/caldav/create")]
public sealed record CreateCalDavCredentialCommand(
    string Name,
    IReadOnlyList<string> ReadableCalendars,
    IReadOnlyList<string> WritableCalendars) : ICommand<CreateCalDavCredentialResult>;

[BluQubeCommand(Path = "commands/admin/credentials/client/rotate")]
public sealed record RotateClientCredentialCommand(Guid CredentialId) : ICommand<RotateClientCredentialResult>;

[BluQubeCommand(Path = "commands/admin/credentials/feed/rotate")]
public sealed record RotateFeedTokenCommand(Guid TokenId) : ICommand<RotateFeedTokenResult>;

[BluQubeCommand(Path = "commands/admin/credentials/caldav/rotate")]
public sealed record RotateCalDavCredentialCommand(Guid CredentialId) : ICommand<RotateCalDavCredentialResult>;

[BluQubeCommand(Path = "commands/admin/credentials/client/revoke")]
public sealed record RevokeClientCredentialCommand(Guid CredentialId) : ICommand<RevokeClientCredentialResult>;

[BluQubeCommand(Path = "commands/admin/credentials/feed/revoke")]
public sealed record RevokeFeedTokenCommand(Guid TokenId) : ICommand<RevokeFeedTokenResult>;

[BluQubeCommand(Path = "commands/admin/credentials/caldav/revoke")]
public sealed record RevokeCalDavCredentialCommand(Guid CredentialId) : ICommand<RevokeCalDavCredentialResult>;

public sealed record ClientCredentialMetadataDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public sealed record FeedTokenMetadataDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> AllowedCalendars,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public sealed record CalDavCredentialMetadataDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> ReadableCalendars,
    IReadOnlyList<string> WritableCalendars,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public interface IGeneratedSecretResult : ICommandResult
{
    Guid Id { get; }

    string Name { get; }

    string Secret { get; }

    string Message { get; }
}

public interface ICredentialMutationResult : ICommandResult
{
    Guid Id { get; }

    string Name { get; }

    string Message { get; }
}

public sealed record CreateClientCredentialResult(
    Guid Id,
    string Name,
    string Secret,
    string Message) : IGeneratedSecretResult;

public sealed record CreateFeedTokenResult(
    Guid Id,
    string Name,
    string Secret,
    string Message) : IGeneratedSecretResult;

public sealed record CreateCalDavCredentialResult(
    Guid Id,
    string Name,
    string Secret,
    string Message) : IGeneratedSecretResult;

public sealed record RotateClientCredentialResult(
    Guid Id,
    string Name,
    string Secret,
    string Message) : IGeneratedSecretResult;

public sealed record RotateFeedTokenResult(
    Guid Id,
    string Name,
    string Secret,
    string Message) : IGeneratedSecretResult;

public sealed record RotateCalDavCredentialResult(
    Guid Id,
    string Name,
    string Secret,
    string Message) : IGeneratedSecretResult;

public sealed record RevokeClientCredentialResult(
    Guid Id,
    string Name,
    string Message) : ICredentialMutationResult;

public sealed record RevokeFeedTokenResult(
    Guid Id,
    string Name,
    string Message) : ICredentialMutationResult;

public sealed record RevokeCalDavCredentialResult(
    Guid Id,
    string Name,
    string Message) : ICredentialMutationResult;
