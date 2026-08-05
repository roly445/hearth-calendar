using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
[Trait("Category", "Docker")]
public sealed class StoreIntentAsyncTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task EventIntentDocument_round_trips_source_payload_actor_and_timestamp()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var intent = Intent(
            "Dentist for Adult A",
            new EventIntentPayload(Today, new TimeOnly(9, 0), new TimeOnly(9, 30)));

        await store.StoreIntentAsync(intent, CancellationToken.None);
        var loaded = await store.LoadIntentAsync(intent.Id, CancellationToken.None);

        await Verifier.Verify(DescribeIntent(loaded));
    }
}
