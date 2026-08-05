using BluQube.Constants;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Features.Credentials;

namespace HearthCalendar.Tests.Server;

public sealed class CreateClientCredentialCommandHandlerHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Creates_hashed_client_credential_and_audit_without_raw_secret()
    {
        var store = new RecordingCredentialStore();
        var handler = new CreateClientCredentialCommandHandler(store);

        var result = await handler.Handle(
            new CreateClientCredentialCommand("home-assistant", [HearthCalendarAuth.IntakeWriteScope]),
            CancellationToken.None);

        var credential = store.ClientCredentials.Single();
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
            Credential = DescribeClientCredential(credential, result.Data.Secret),
            Audit = DescribeAudit(audit, result.Data.Secret)
        });
    }
}
