using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class NoOpCredentialStore : IHearthCalendarCredentialStore
{
    public Task<CredentialInventory> QueryInventoryAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new CredentialInventory([], [], []));

    public Task<ClientCredentialDocument?> FindActiveClientCredentialAsync(
        string secret,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult<ClientCredentialDocument?>(null);

    public Task<FeedTokenDocument?> FindActiveFeedTokenAsync(
        string token,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult<FeedTokenDocument?>(null);

    public Task<CalDavCredentialDocument?> FindActiveCalDavCredentialAsync(
        string name,
        string secret,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult<CalDavCredentialDocument?>(null);

    public Task StoreClientCredentialAsync(
        ClientCredentialDocument credential,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ClientCredentialDocument?> LoadClientCredentialAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task StoreFeedTokenAsync(
        FeedTokenDocument token,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<FeedTokenDocument?> LoadFeedTokenAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task StoreCalDavCredentialAsync(
        CalDavCredentialDocument credential,
        AuditEntry audit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CalDavCredentialDocument?> LoadCalDavCredentialAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
