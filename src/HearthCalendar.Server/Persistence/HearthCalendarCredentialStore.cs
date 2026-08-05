using HearthCalendar.Server.Domain;
using Marten;

namespace HearthCalendar.Server.Persistence;

public interface IHearthCalendarCredentialStore
{
    Task<CredentialInventory> QueryInventoryAsync(CancellationToken cancellationToken);

    Task<ClientCredentialDocument?> FindActiveClientCredentialAsync(string secret, DateTimeOffset usedAt, CancellationToken cancellationToken);

    Task<FeedTokenDocument?> FindActiveFeedTokenAsync(string token, DateTimeOffset usedAt, CancellationToken cancellationToken);

    Task<CalDavCredentialDocument?> FindActiveCalDavCredentialAsync(
        string name,
        string secret,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken);

    Task StoreClientCredentialAsync(ClientCredentialDocument credential, AuditEntry audit, CancellationToken cancellationToken);

    Task<ClientCredentialDocument?> LoadClientCredentialAsync(Guid id, CancellationToken cancellationToken);

    Task StoreFeedTokenAsync(FeedTokenDocument token, AuditEntry audit, CancellationToken cancellationToken);

    Task<FeedTokenDocument?> LoadFeedTokenAsync(Guid id, CancellationToken cancellationToken);

    Task StoreCalDavCredentialAsync(CalDavCredentialDocument credential, AuditEntry audit, CancellationToken cancellationToken);

    Task<CalDavCredentialDocument?> LoadCalDavCredentialAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record CredentialInventory(
    IReadOnlyList<ClientCredentialDocument> ClientCredentials,
    IReadOnlyList<FeedTokenDocument> FeedTokens,
    IReadOnlyList<CalDavCredentialDocument> CalDavCredentials);

public sealed class MartenHearthCalendarCredentialStore(IDocumentSession session) : IHearthCalendarCredentialStore
{
    public async Task<CredentialInventory> QueryInventoryAsync(CancellationToken cancellationToken)
    {
        var clients = await session.Query<ClientCredentialDocument>()
            .OrderBy(credential => credential.ClientName)
            .ToListAsync(cancellationToken);
        var feedTokens = await session.Query<FeedTokenDocument>()
            .OrderBy(token => token.Name)
            .ToListAsync(cancellationToken);
        var calDavCredentials = await session.Query<CalDavCredentialDocument>()
            .OrderBy(credential => credential.Name)
            .ToListAsync(cancellationToken);

        return new CredentialInventory(clients, feedTokens, calDavCredentials);
    }

    public async Task<ClientCredentialDocument?> FindActiveClientCredentialAsync(
        string secret,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken)
    {
        var credentials = await session.Query<ClientCredentialDocument>()
            .Where(credential => credential.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var match = credentials.FirstOrDefault(credential =>
            HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(secret, credential.SecretHash));
        if (match is null)
        {
            return null;
        }

        var touched = match with { LastUsedAt = usedAt };
        session.Store(touched);
        await session.SaveChangesAsync(cancellationToken);

        return touched;
    }

    public async Task<FeedTokenDocument?> FindActiveFeedTokenAsync(
        string token,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken)
    {
        var tokens = await session.Query<FeedTokenDocument>()
            .Where(feedToken => feedToken.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var match = tokens.FirstOrDefault(feedToken =>
            HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(token, feedToken.TokenHash));
        if (match is null)
        {
            return null;
        }

        var touched = match with { LastUsedAt = usedAt };
        session.Store(touched);
        await session.SaveChangesAsync(cancellationToken);

        return touched;
    }

    public async Task<CalDavCredentialDocument?> FindActiveCalDavCredentialAsync(
        string name,
        string secret,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken)
    {
        var credentials = await session.Query<CalDavCredentialDocument>()
            .Where(credential => credential.Name == name && credential.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var match = credentials.FirstOrDefault(credential =>
            HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(secret, credential.SecretHash));
        if (match is null)
        {
            return null;
        }

        var touched = match with { LastUsedAt = usedAt };
        session.Store(touched);
        await session.SaveChangesAsync(cancellationToken);

        return touched;
    }

    public Task StoreClientCredentialAsync(
        ClientCredentialDocument credential,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        StoreWithAuditAsync(credential, audit, cancellationToken);

    public Task<ClientCredentialDocument?> LoadClientCredentialAsync(Guid id, CancellationToken cancellationToken) =>
        session.LoadAsync<ClientCredentialDocument>(id, cancellationToken);

    public Task StoreFeedTokenAsync(
        FeedTokenDocument token,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        StoreWithAuditAsync(token, audit, cancellationToken);

    public Task<FeedTokenDocument?> LoadFeedTokenAsync(Guid id, CancellationToken cancellationToken) =>
        session.LoadAsync<FeedTokenDocument>(id, cancellationToken);

    public Task StoreCalDavCredentialAsync(
        CalDavCredentialDocument credential,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        StoreWithAuditAsync(credential, audit, cancellationToken);

    public Task<CalDavCredentialDocument?> LoadCalDavCredentialAsync(Guid id, CancellationToken cancellationToken) =>
        session.LoadAsync<CalDavCredentialDocument>(id, cancellationToken);

    private async Task StoreWithAuditAsync<TDocument>(
        TDocument document,
        AuditEntry audit,
        CancellationToken cancellationToken)
        where TDocument : notnull
    {
        session.Store(document);
        session.Store(audit.ToDocument());

        await session.SaveChangesAsync(cancellationToken);
    }
}
