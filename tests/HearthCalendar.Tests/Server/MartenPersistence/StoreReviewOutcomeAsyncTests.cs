using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
[Trait("Category", "Docker")]
public sealed class StoreReviewOutcomeAsyncTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task ReviewWorkflow_persists_intent_decision_event_suggestion_and_audit_in_one_marten_commit()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var intent = Intent("dentist");
        var outcome = await Pipeline(new StubAiReviewProvider(Suggestion())).ReviewWithAuditAsync(
            intent,
            CancellationToken.None);

        await store.StoreReviewOutcomeAsync(intent, outcome, CancellationToken.None);
        var approvedEvents = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var audits = await store.QueryAuditEntriesAsync(CancellationToken.None);
        var loadedOutcome = await store.LoadReviewOutcomeAsync(outcome.Decision.Id, CancellationToken.None);

        await Verifier.Verify(new
        {
            ApprovedEvents = approvedEvents.Select(DescribeEvent),
            Audits = audits.Select(DescribeAudit),
            LoadedOutcome = DescribeOutcome(loadedOutcome)
        });
    }
}
