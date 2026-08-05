using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class GetCredentialInventoryQueryProcessorHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Lists_credential_metadata_without_secret_hashes()
    {
        var store = new RecordingCredentialStore();
        store.ClientCredentials.Add(new ClientCredentialDocument
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            ClientName = "home-assistant",
            SecretHash = "sha256:secret-hash",
            Scopes = ["intake:write"],
            CreatedAt = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero)
        });
        store.FeedTokens.Add(new FeedTokenDocument
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Name = "family-feed",
            TokenHash = "sha256:token-hash",
            AllowedCalendars = ["Family", "Combined"],
            Scopes = ["feed:read"],
            CreatedAt = new DateTimeOffset(2026, 8, 5, 9, 5, 0, TimeSpan.Zero),
            LastUsedAt = new DateTimeOffset(2026, 8, 5, 9, 10, 0, TimeSpan.Zero)
        });
        store.CalDavCredentials.Add(new CalDavCredentialDocument
        {
            Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Name = "phone-caldav",
            SecretHash = "sha256:caldav-hash",
            ReadableCalendars = ["adult-a", "combined"],
            WritableCalendars = ["smart-inbox"],
            Scopes = ["caldav:read", "caldav:write"],
            CreatedAt = new DateTimeOffset(2026, 8, 5, 9, 15, 0, TimeSpan.Zero),
            RevokedAt = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero)
        });
        var processor = new GetCredentialInventoryQueryProcessor(store);

        var result = await processor.Handle(new GetCredentialInventoryQuery(), CancellationToken.None);

        await Verifier.Verify(new
        {
            result.Status,
            result.Data
        });
    }
}
