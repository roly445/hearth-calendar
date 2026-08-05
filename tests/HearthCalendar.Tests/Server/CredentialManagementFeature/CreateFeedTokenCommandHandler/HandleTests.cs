using BluQube.Constants;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Features.Credentials;

namespace HearthCalendar.Tests.Server;

public sealed class CreateFeedTokenCommandHandlerHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Creates_hashed_feed_token_with_calendar_scope_and_audit_without_raw_secret()
    {
        var store = new RecordingCredentialStore();
        var handler = new CreateFeedTokenCommandHandler(store);

        var result = await handler.Handle(
            new CreateFeedTokenCommand("adult-a-work", ["AdultA", "Combined"]),
            CancellationToken.None);

        var token = store.FeedTokens.Single();
        var audit = store.Audits.Single();
        await Verifier.Verify(new
        {
            result.Status,
            Result = result.Status == CommandResultStatus.Succeeded
                ? new
                {
                    result.Data.Name,
                    HasSecret = !string.IsNullOrWhiteSpace(result.Data.Secret),
                    SecretHasPrefix = result.Data.Secret.StartsWith("hc_feed_", StringComparison.Ordinal),
                    result.Data.Message
                }
                : null,
            Token = DescribeFeedToken(token, result.Data.Secret),
            Audit = DescribeAudit(audit, result.Data.Secret)
        });
    }
}
