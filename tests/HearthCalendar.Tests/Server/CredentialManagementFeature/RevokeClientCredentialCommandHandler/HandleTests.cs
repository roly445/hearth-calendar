using BluQube.Constants;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class RevokeClientCredentialCommandHandlerHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Revokes_client_credential_and_audits_without_secret()
    {
        var store = new RecordingCredentialStore();
        var credential = new ClientCredentialDocument
        {
            Id = Guid.NewGuid(),
            ClientName = "home-assistant",
            SecretHash = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Hash("client-secret"),
            Scopes = ["intake:write"],
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        store.ClientCredentials.Add(credential);
        var handler = new RevokeClientCredentialCommandHandler(store);

        var result = await handler.Handle(new RevokeClientCredentialCommand(credential.Id), CancellationToken.None);

        var revoked = store.ClientCredentials.Single();
        await Verifier.Verify(new
        {
            result.Status,
            Result = result.Status == CommandResultStatus.Succeeded ? result.Data : null,
            HasRevokedAt = revoked.RevokedAt is not null,
            Audit = DescribeAudit(store.Audits.Single(), "client-secret")
        });
    }
}
