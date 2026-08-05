using BluQube.Constants;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Features.Credentials;

namespace HearthCalendar.Tests.Server;

public sealed class CreateCalDavCredentialCommandHandlerHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Creates_hashed_caldav_credential_with_read_write_calendars()
    {
        var store = new RecordingCredentialStore();
        var handler = new CreateCalDavCredentialCommandHandler(store);

        var result = await handler.Handle(
            new CreateCalDavCredentialCommand("phone-caldav", ["adult-a"], ["smart-inbox"]),
            CancellationToken.None);

        var credential = store.CalDavCredentials.Single();
        var audit = store.Audits.Single();
        await Verifier.Verify(new
        {
            result.Status,
            Result = result.Status == CommandResultStatus.Succeeded
                ? new
                {
                    result.Data.Name,
                    HasSecret = !string.IsNullOrWhiteSpace(result.Data.Secret),
                    SecretHasPrefix = result.Data.Secret.StartsWith("hc_caldav_", StringComparison.Ordinal),
                    result.Data.Message
                }
                : null,
            Credential = DescribeCalDavCredential(credential, result.Data.Secret),
            Audit = DescribeAudit(audit, result.Data.Secret)
        });
    }
}
