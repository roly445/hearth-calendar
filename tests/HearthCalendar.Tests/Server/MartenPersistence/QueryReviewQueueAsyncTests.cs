using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
public sealed class QueryReviewQueueAsyncTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task ReviewQueueQuery_returns_staged_decisions_only()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var stagedIntent = Intent("dentist");
        var stagedOutcome = Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAudit(stagedIntent);
        var stagedCandidateIntent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(Today.AddDays(-1), new TimeOnly(9, 0), new TimeOnly(9, 30)));
        var stagedCandidateOutcome = Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAudit(stagedCandidateIntent);
        var approvedIntent = Intent("Family BBQ");
        var approvedOutcome = Pipeline(NoOpAiReviewProvider.Instance).ReviewWithAudit(approvedIntent);

        await store.StoreReviewOutcomeAsync(stagedIntent, stagedOutcome, CancellationToken.None);
        await store.StoreReviewOutcomeAsync(stagedCandidateIntent, stagedCandidateOutcome, CancellationToken.None);
        await store.StoreReviewOutcomeAsync(approvedIntent, approvedOutcome, CancellationToken.None);
        var reviewQueue = await store.QueryReviewQueueAsync(CancellationToken.None);

        await Verifier.Verify(reviewQueue.Select(DescribeDecision));
    }
}
