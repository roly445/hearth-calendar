using HearthCalendar.Server.Domain;

namespace HearthCalendar.Tests.Domain;

public abstract class EventMutationPolicyTestBase
{
    protected static readonly DateOnly Today = new(2026, 7, 30);

    protected static CalendarEvent AdultAEvent(
        string title,
        DateOnly date,
        TimeOnly? startTime,
        TimeOnly? endTime) =>
        CalendarEvent.Approved(
            CalendarEventId.New(),
            title,
            new EventTime(date, startTime, endTime, startTime is null && endTime is null),
            VirtualCalendar.AdultA,
            EventCategory.Personal,
            BusyStatus.Busy,
            [new Participant(KnownPeople.AdultA, ParticipationRole.Attendee, BusyStatus.Busy)],
            CalendarSource.Test);
}
