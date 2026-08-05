using BluQube.Constants;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Persistence;

namespace HearthCalendar.Tests.Server;

public sealed class RotateFeedTokenCommandHandlerHandleTests : CredentialManagementFeatureTestBase
{
    [Fact]
    public async Task Rotates_token_hash_without_changing_calendar_scope()
    {
        var store = new RecordingCredentialStore();
        var originalSecret = "old-feed-token";
        var token = new FeedTokenDocument
        {
            Id = Guid.NewGuid(),
            Name = "family-feed",
            TokenHash = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Hash(originalSecret),
            AllowedCalendars = ["Family", "Combined"],
            Scopes = ["feed:read"],
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        store.FeedTokens.Add(token);
        var handler = new RotateFeedTokenCommandHandler(store);

        var result = await handler.Handle(new RotateFeedTokenCommand(token.Id), CancellationToken.None);

        var rotated = store.FeedTokens.Single();
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
            Token = DescribeFeedToken(rotated, result.Data.Secret),
            OldSecretStillMatches = HearthCalendar.Server.Auth.HearthCalendarSecretHasher.Matches(originalSecret, rotated.TokenHash),
            Audit = DescribeAudit(audit, result.Data.Secret)
        });
    }
}
