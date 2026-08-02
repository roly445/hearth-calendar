using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
public sealed class DeleteApprovedEventAsyncTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task DeleteApprovedEvent_removes_event_and_writes_audit()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var approved = AdultAEvent("Dentist for Adult A", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));

        session.Store(approved.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);

        await store.DeleteApprovedEventAsync(
            approved,
            new AuditEntry(
                AuditEntryId.New(),
                AuditAction.EventDeleted,
                ActorRef.System,
                SubmittedAt(),
                "Approved event deleted.",
                CalendarEventId: approved.Id),
            CancellationToken.None);

        var remaining = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var audits = await store.QueryAuditEntriesAsync(CancellationToken.None);

        await Verifier.Verify(new
        {
            Remaining = remaining.Select(DescribeEvent),
            Audits = audits.Select(DescribeAudit)
        });
    }

    [Fact]
    public async Task DeleteApprovedEvent_rejects_when_approved_event_changed_after_match()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var approved = AdultAEvent("Dentist for Adult A", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var changed = approved with { Title = "Updated appointment for Adult A" };

        session.Store(approved.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);
        session.Store(changed.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<StaleApprovedEventMutationException>(() =>
            store.DeleteApprovedEventAsync(
                approved,
                new AuditEntry(
                    AuditEntryId.New(),
                    AuditAction.EventDeleted,
                    ActorRef.System,
                    SubmittedAt(),
                    "Approved event deleted.",
                    CalendarEventId: approved.Id),
                CancellationToken.None));

        var remaining = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var audits = await store.QueryAuditEntriesAsync(CancellationToken.None);

        var current = Assert.Single(remaining);
        Assert.Equal(changed.Title, current.Title);
        Assert.Empty(audits);
    }
}
