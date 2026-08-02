using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
public sealed class UpsertCalDavObjectAsyncTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task CalDavObject_upsert_reuses_identical_retry_and_replaces_changed_content()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var firstIntent = CalDavIntent("Family planning");
        var retryIntent = CalDavIntent("Family planning", minutesOffset: 1);
        var changedIntent = CalDavIntent("Updated family planning", minutesOffset: 2);

        var first = await store.UpsertCalDavObjectAsync(
            CalDavUpsert("family-planning", "hash-1", "\"hash-1\"", firstIntent),
            CancellationToken.None);
        var retry = await store.UpsertCalDavObjectAsync(
            CalDavUpsert("family-planning", "hash-1", "\"hash-1\"", retryIntent),
            CancellationToken.None);
        var changed = await store.UpsertCalDavObjectAsync(
            CalDavUpsert("family-planning", "hash-2", "\"hash-2\"", changedIntent),
            CancellationToken.None);

        var objects = await session.Query<CalDavObjectDocument>().ToListAsync(CancellationToken.None);
        var intents = new[]
        {
            await store.LoadIntentAsync(first.IntentId!.Value, CancellationToken.None),
            await store.LoadIntentAsync(retry.IntentId!.Value, CancellationToken.None),
            await store.LoadIntentAsync(changed.IntentId!.Value, CancellationToken.None),
            await store.LoadIntentAsync(retryIntent.Id, CancellationToken.None)
        };
        var audits = await store.QueryAuditEntriesAsync(CancellationToken.None);
        var approvedEvents = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.Combined,
            CancellationToken.None);

        await Verifier.Verify(new
        {
            Results = new[]
            {
                DescribeCalDavUpsert(first),
                DescribeCalDavUpsert(retry),
                DescribeCalDavUpsert(changed)
            },
            Objects = objects.Select(DescribeCalDavObject),
            Intents = intents.Select(DescribeIntent),
            ApprovedEvents = approvedEvents.Select(DescribeEvent),
            Audits = audits.Select(DescribeAudit)
        });
    }
}
