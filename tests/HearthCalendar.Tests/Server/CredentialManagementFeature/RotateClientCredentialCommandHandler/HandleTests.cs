using BluQube.Constants;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class RotateClientCredentialCommandHandlerHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Rotates_secret_hash_and_reopens_revoked_client_credential()
    {
        var store = new RecordingCredentialStore();
        var originalSecret = "old-client-secret";
        var credential = new ClientCredentialDocument
        {
            Id = Guid.NewGuid(),
            ClientName = "home-assistant",
            SecretHash = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Hash(originalSecret),
            Scopes = ["intake:write"],
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        store.ClientCredentials.Add(credential);
        var handler = new RotateClientCredentialCommandHandler(store);

        var result = await handler.Handle(new RotateClientCredentialCommand(credential.Id), CancellationToken.None);

        var rotated = store.ClientCredentials.Single();
        var audit = store.Audits.Single();
        await Verifier.Verify(new
        {
            result.Status,
            Result = result.Status == CommandResultStatus.Succeeded
                ? new
                {
                    result.Data.Name,
                    HasSecret = !string.IsNullOrWhiteSpace(result.Data.Secret),
                    SecretHasPrefix = result.Data.Secret.StartsWith("hc_client_", StringComparison.Ordinal),
                    result.Data.Message
                }
                : null,
            Credential = DescribeClientCredential(rotated, result.Data.Secret),
            OldSecretStillMatches = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(originalSecret, rotated.SecretHash),
            Audit = DescribeAudit(audit, result.Data.Secret)
        });
    }
}
