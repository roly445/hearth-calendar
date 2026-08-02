using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
public sealed class CredentialDocumentPersistenceTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task Credential_and_feed_token_documents_store_hashes_without_raw_secrets()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        session.Store(new ClientCredentialDocument
        {
            Id = Guid.NewGuid(),
            ClientName = "home-assistant",
            SecretHash = "sha256:credential-hash-placeholder",
            Scopes = ["intake:write"],
            CreatedAt = SubmittedAt(),
            LastUsedAt = SubmittedAt().AddMinutes(5)
        });
        session.Store(new FeedTokenDocument
        {
            Id = Guid.NewGuid(),
            Name = "adult-a-feed",
            TokenHash = "sha256:feed-hash-placeholder",
            AllowedCalendars = [VirtualCalendar.AdultA.ToString()],
            Scopes = ["feed:adult-a"],
            CreatedAt = SubmittedAt(),
            LastUsedAt = SubmittedAt().AddMinutes(10)
        });
        session.Store(new CalDavCredentialDocument
        {
            Id = Guid.NewGuid(),
            Name = "caldav-app",
            SecretHash = "sha256:caldav-hash-placeholder",
            WritableCalendars = ["smart-inbox"],
            Scopes = ["caldav:write"],
            CreatedAt = SubmittedAt(),
            LastUsedAt = SubmittedAt().AddMinutes(15)
        });
        await session.SaveChangesAsync(CancellationToken.None);

        var credentials = await session.Query<ClientCredentialDocument>().ToListAsync(CancellationToken.None);
        var feedTokens = await session.Query<FeedTokenDocument>().ToListAsync(CancellationToken.None);
        var calDavCredentials = await session.Query<CalDavCredentialDocument>().ToListAsync(CancellationToken.None);

        await Verifier.Verify(new
        {
            Credentials = credentials.Select(credential => new
            {
                credential.ClientName,
                HasSecretHash = !string.IsNullOrWhiteSpace(credential.SecretHash),
                credential.Scopes,
                LastUsedAt = credential.LastUsedAt?.ToString("O"),
                credential.RevokedAt
            }),
            FeedTokens = feedTokens.Select(feedToken => new
            {
                feedToken.Name,
                HasTokenHash = !string.IsNullOrWhiteSpace(feedToken.TokenHash),
                feedToken.AllowedCalendars,
                feedToken.Scopes,
                LastUsedAt = feedToken.LastUsedAt?.ToString("O"),
                feedToken.RevokedAt
            }),
            CalDavCredentials = calDavCredentials.Select(credential => new
            {
                credential.Name,
                HasSecretHash = !string.IsNullOrWhiteSpace(credential.SecretHash),
                credential.WritableCalendars,
                credential.Scopes,
                LastUsedAt = credential.LastUsedAt?.ToString("O"),
                credential.RevokedAt
            })
        });
    }
}
