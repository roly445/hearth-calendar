namespace HearthCalendar.Server.Domain;

public static class VirtualCalendarViews
{
    public static IReadOnlyList<CalendarEvent> ForCalendar(VirtualCalendar calendar, IReadOnlyList<CalendarEvent> events) =>
        calendar == VirtualCalendar.Review
            ? events.Where(calendarEvent => calendarEvent.ReviewStatus == ReviewStatus.Staged).ToArray()
            : events
                .Where(calendarEvent => calendarEvent.ReviewStatus == ReviewStatus.Approved)
                .Where(calendarEvent => BelongsTo(calendarEvent, calendar))
                .ToArray();

    private static bool BelongsTo(CalendarEvent calendarEvent, VirtualCalendar calendar) =>
        calendar switch
        {
            VirtualCalendar.Combined => true,
            VirtualCalendar.Events => calendarEvent.PrimaryCalendar == VirtualCalendar.Events,
            VirtualCalendar.Family => calendarEvent.PrimaryCalendar == VirtualCalendar.Family,
            VirtualCalendar.Child => calendarEvent.PrimaryCalendar == VirtualCalendar.Child ||
                calendarEvent.Participants.Any(participant => participant.Person.Id == KnownPeople.Child.Id),
            VirtualCalendar.AdultA => IsParticipantCalendarEvent(calendarEvent, KnownPeople.AdultA),
            VirtualCalendar.AdultB => IsParticipantCalendarEvent(calendarEvent, KnownPeople.AdultB),
            VirtualCalendar.Review => calendarEvent.ReviewStatus == ReviewStatus.Staged,
            _ => false
        };

    private static bool IsParticipantCalendarEvent(CalendarEvent calendarEvent, Person person) =>
        calendarEvent.PrimaryCalendar == CalendarFor(person) ||
        calendarEvent.Participants.Any(participant => participant.Person.Id == person.Id && participant.BusyStatus == BusyStatus.Busy);

    private static VirtualCalendar CalendarFor(Person person)
    {
        if (person.Id == KnownPeople.AdultA.Id)
        {
            return VirtualCalendar.AdultA;
        }

        return person.Id == KnownPeople.AdultB.Id ? VirtualCalendar.AdultB : VirtualCalendar.Child;
    }
}
