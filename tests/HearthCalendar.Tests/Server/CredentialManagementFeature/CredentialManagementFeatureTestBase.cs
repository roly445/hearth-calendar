using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public abstract class CredentialManagementFeatureTestBase
{
    protected sealed class RecordingCredentialStore : IHearthCalendarCredentialStore
    {
        public List<ClientCredentialDocument> ClientCredentials { get; } = [];

        public List<FeedTokenDocument> FeedTokens { get; } = [];

        public List<CalDavCredentialDocument> CalDavCredentials { get; } = [];

        public List<AuditEntry> Audits { get; } = [];

        public Task<CredentialInventory> QueryInventoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CredentialInventory(ClientCredentials, FeedTokens, CalDavCredentials));

        public Task<ClientCredentialDocument?> FindActiveClientCredentialAsync(
            string secret,
            DateTimeOffset usedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FeedTokenDocument?> FindActiveFeedTokenAsync(
            string token,
            DateTimeOffset usedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CalDavCredentialDocument?> FindActiveCalDavCredentialAsync(
            string name,
            string secret,
            DateTimeOffset usedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task StoreClientCredentialAsync(
            ClientCredentialDocument credential,
            AuditEntry audit,
            CancellationToken cancellationToken)
        {
            ClientCredentials.RemoveAll(candidate => candidate.Id == credential.Id);
            ClientCredentials.Add(credential);
            Audits.Add(audit);

            return Task.CompletedTask;
        }

        public Task<ClientCredentialDocument?> LoadClientCredentialAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ClientCredentials.SingleOrDefault(candidate => candidate.Id == id));

        public Task StoreFeedTokenAsync(
            FeedTokenDocument token,
            AuditEntry audit,
            CancellationToken cancellationToken)
        {
            FeedTokens.RemoveAll(candidate => candidate.Id == token.Id);
            FeedTokens.Add(token);
            Audits.Add(audit);

            return Task.CompletedTask;
        }

        public Task<FeedTokenDocument?> LoadFeedTokenAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(FeedTokens.SingleOrDefault(candidate => candidate.Id == id));

        public Task StoreCalDavCredentialAsync(
            CalDavCredentialDocument credential,
            AuditEntry audit,
            CancellationToken cancellationToken)
        {
            CalDavCredentials.RemoveAll(candidate => candidate.Id == credential.Id);
            CalDavCredentials.Add(credential);
            Audits.Add(audit);

            return Task.CompletedTask;
        }

        public Task<CalDavCredentialDocument?> LoadCalDavCredentialAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(CalDavCredentials.SingleOrDefault(candidate => candidate.Id == id));
    }

    protected static object DescribeClientCredential(ClientCredentialDocument credential, string rawSecret) => new
    {
        credential.ClientName,
        HasSecretHash = !string.IsNullOrWhiteSpace(credential.SecretHash),
        HashMatchesGeneratedSecret = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(rawSecret, credential.SecretHash),
        SecretHashContainsGeneratedSecret = credential.SecretHash.Contains(rawSecret, StringComparison.Ordinal),
        credential.Scopes,
        HasCreatedAt = credential.CreatedAt != default,
        credential.LastUsedAt,
        credential.RevokedAt
    };

    protected static object DescribeFeedToken(FeedTokenDocument token, string rawSecret) => new
    {
        token.Name,
        HasTokenHash = !string.IsNullOrWhiteSpace(token.TokenHash),
        HashMatchesGeneratedSecret = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(rawSecret, token.TokenHash),
        TokenHashContainsGeneratedSecret = token.TokenHash.Contains(rawSecret, StringComparison.Ordinal),
        token.AllowedCalendars,
        token.Scopes,
        HasCreatedAt = token.CreatedAt != default,
        token.LastUsedAt,
        token.RevokedAt
    };

    protected static object DescribeCalDavCredential(CalDavCredentialDocument credential, string rawSecret) => new
    {
        credential.Name,
        HasSecretHash = !string.IsNullOrWhiteSpace(credential.SecretHash),
        HashMatchesGeneratedSecret = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(rawSecret, credential.SecretHash),
        SecretHashContainsGeneratedSecret = credential.SecretHash.Contains(rawSecret, StringComparison.Ordinal),
        credential.ReadableCalendars,
        credential.WritableCalendars,
        credential.Scopes,
        HasCreatedAt = credential.CreatedAt != default,
        credential.LastUsedAt,
        credential.RevokedAt
    };

    protected static object DescribeAudit(AuditEntry audit, string rawSecret) => new
    {
        Action = audit.Action.ToString(),
        Actor = audit.Actor.Id,
        HasOccurredAt = audit.OccurredAt != default,
        audit.Summary,
        audit.Metadata,
        ContainsRawSecret = audit.Metadata?.Values.Any(value => value.Contains(rawSecret, StringComparison.Ordinal)) == true
    };
}
