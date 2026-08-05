using HearthCalendar.Server.Domain;
using HearthCalendar.Server.Persistence;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HearthCalendar.Tests.Server;

[Collection(MartenPostgreSqlCollection.Name)]
[Trait("Category", "Docker")]
public sealed class QueryApprovedEventsAsyncTests(MartenPostgreSqlFixture fixture) : MartenPersistenceTestBase(fixture)
{
    [Fact]
    public async Task ApprovedEventsQuery_excludes_staged_rejected_and_other_calendar_items()
    {
        await using var services = CreateServices();
        using var scope = services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var approvedAdultA = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Dentist for Adult A",
            new EventTime(Today, new TimeOnly(9, 0), new TimeOnly(9, 30), false),
            VirtualCalendar.AdultA,
            EventCategory.Personal,
            BusyStatus.Busy,
            [new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy)],
            CalendarSource.Test);
        var familyEvent = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Family BBQ",
            new EventTime(Today, null, null, true),
            VirtualCalendar.Family,
            EventCategory.Family,
            BusyStatus.Busy,
            KnownPeople.All.Select(person => new Participant(person, ParticipationRole.Attendee, BusyStatus.Busy)).ToArray(),
            CalendarSource.Test);
        var stagedAdultA = approvedAdultA with
        {
            Id = CalendarEventId.New(),
            Title = "Staged Adult A item",
            ReviewStatus = ReviewStatus.Staged
        };
        var rejectedAdultA = approvedAdultA with
        {
            Id = CalendarEventId.New(),
            Title = "Rejected Adult A item",
            ReviewStatus = ReviewStatus.Rejected
        };
        var eventsReference = CalendarEvent.Approved(
            CalendarEventId.New(),
            "Adult B birthday",
            new EventTime(Today, null, null, true),
            VirtualCalendar.Events,
            EventCategory.Birthday,
            BusyStatus.Free,
            [new Participant(KnownPeople.AdultB, ParticipationRole.Attendee, BusyStatus.Free)],
            CalendarSource.Test,
            new RecurrenceRule(RecurrenceFrequency.Yearly));

        session.Store(approvedAdultA.ToDocument());
        session.Store(familyEvent.ToDocument());
        session.Store(stagedAdultA.ToDocument());
        session.Store(rejectedAdultA.ToDocument());
        session.Store(eventsReference.ToDocument());
        await session.SaveChangesAsync(CancellationToken.None);

        var store = scope.ServiceProvider.GetRequiredService<IHearthCalendarStore>();
        var adultAEvents = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var eventReferenceEvents = await store.QueryApprovedEventsAsync(
            Today,
            Today,
            VirtualCalendar.Events,
            CancellationToken.None);
        var loadedAdultA = await store.LoadApprovedEventAsync(
            approvedAdultA.Id,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var hiddenStaged = await store.LoadApprovedEventAsync(
            stagedAdultA.Id,
            VirtualCalendar.AdultA,
            CancellationToken.None);
        var hiddenOtherCalendar = await store.LoadApprovedEventAsync(
            approvedAdultA.Id,
            VirtualCalendar.AdultB,
            CancellationToken.None);

        Assert.Equal(approvedAdultA.Id, loadedAdultA?.Id);
        Assert.Null(hiddenStaged);
        Assert.Null(hiddenOtherCalendar);

        await Verifier.Verify(new
        {
            AdultA = adultAEvents.Select(DescribeEvent),
            Events = eventReferenceEvents.Select(DescribeEvent)
        });
    }
}
