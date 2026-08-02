using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
public sealed class RescheduleApprovedEventAsyncTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task RescheduleApprovedEvent_updates_existing_event_and_writes_audit()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var approved = AdultAEvent("Dentist for Adult A", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var rescheduled = approved with
        {
            Time = new EventTime(Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(10, 30), false)
        };

        session.Store(approved.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);

        await store.RescheduleApprovedEventAsync(
            approved,
            rescheduled,
            new AuditEntry(
                AuditEntryId.New(),
                AuditAction.EventRescheduled,
                ActorRef.System,
                SubmittedAt(),
                "Approved event rescheduled.",
                CalendarEventId: approved.Id),
            CancellationToken.None);

        var originalDate = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var newDate = await store.QueryApprovedEventsAsync(
            Today.AddDays(1),
            Today.AddDays(1),
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var audits = await store.QueryAuditEntriesAsync(CancellationToken.None);

        await Verifier.Verify(new
        {
            OriginalDate = originalDate.Select(DescribeEvent),
            NewDate = newDate.Select(DescribeEvent),
            Audits = audits.Select(DescribeAudit)
        });
    }

    [Fact]
    public async Task RescheduleApprovedEvent_rejects_when_approved_event_changed_after_match()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var approved = AdultAEvent("Dentist for Adult A", Today, new TimeOnly(9, 0), new TimeOnly(9, 30));
        var changed = approved with { Title = "Updated appointment for Adult A" };
        var rescheduled = approved with
        {
            Time = new EventTime(Today.AddDays(1), new TimeOnly(10, 0), new TimeOnly(10, 30), false)
        };

        session.Store(approved.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);
        session.Store(changed.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<StaleApprovedEventMutationException>(() =>
            store.RescheduleApprovedEventAsync(
                approved,
                rescheduled,
                new AuditEntry(
                    AuditEntryId.New(),
                    AuditAction.EventRescheduled,
                    ActorRef.System,
                    SubmittedAt(),
                    "Approved event rescheduled.",
                    CalendarEventId: approved.Id),
                CancellationToken.None));

        var originalDate = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var newDate = await store.QueryApprovedEventsAsync(
            Today.AddDays(1),
            Today.AddDays(1),
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var audits = await store.QueryAuditEntriesAsync(CancellationToken.None);

        var current = Assert.Single(originalDate);
        Assert.Equal(changed.Title, current.Title);
        Assert.Empty(newDate);
        Assert.Empty(audits);
    }
}
