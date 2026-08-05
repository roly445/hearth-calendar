using BluQube.Constants;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class RevokeFeedTokenCommandHandlerHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Revokes_feed_token_and_audits_without_secret()
    {
        var store = new RecordingCredentialStore();
        var token = new FeedTokenDocument
        {
            Id = Guid.NewGuid(),
            Name = "family-feed",
            TokenHash = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Hash("feed-secret"),
            AllowedCalendars = ["Family"],
            Scopes = ["feed:read"],
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        store.FeedTokens.Add(token);
        var handler = new RevokeFeedTokenCommandHandler(store);

        var result = await handler.Handle(new RevokeFeedTokenCommand(token.Id), CancellationToken.None);

        var revoked = store.FeedTokens.Single();
        await Verifier.Verify(new
        {
            result.Status,
            Result = result.Status == CommandResultStatus.Succeeded ? result.Data : null,
            HasRevokedAt = revoked.RevokedAt is not null,
            Audit = DescribeAudit(store.Audits.Single(), "feed-secret")
        });
    }
}
