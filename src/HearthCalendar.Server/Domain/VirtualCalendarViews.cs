namespace HearthCalendar.Server.Domain;

public static class VirtualCalendarViews
{
    public static IReadOnlyList<CalendarEvent> ForCalendar(
        VirtualCalendar calendar,
        IReadOnlyList<CalendarEvent> events,
        IHouseholdMetadata? householdMetadata = null)
    {
        householdMetadata ??= DefaultHouseholdMetadata.Instance;

        return calendar == VirtualCalendar.Review
            ? events.Where(calendarEvent => calendarEvent.ReviewStatus == ReviewStatus.Staged).ToArray()
            : events
                .Where(calendarEvent => calendarEvent.ReviewStatus == ReviewStatus.Approved)
                .Where(calendarEvent => BelongsTo(calendarEvent, calendar, householdMetadata))
                .ToArray();
    }

    private static bool BelongsTo(
        CalendarEvent calendarEvent,
        VirtualCalendar calendar,
        IHouseholdMetadata householdMetadata) =>
        calendar == VirtualCalendar.Review
            ? calendarEvent.ReviewStatus == ReviewStatus.Staged
            : calendar switch
        {
            VirtualCalendar.Combined => true,
            VirtualCalendar.Events => calendarEvent.PrimaryCalendar == VirtualCalendar.Events,
            VirtualCalendar.Family => calendarEvent.PrimaryCalendar == VirtualCalendar.Family,
            VirtualCalendar.Child => calendarEvent.PrimaryCalendar == VirtualCalendar.Child ||
                calendarEvent.Participants.Any(participant => householdMetadata.IsChild(participant.Person)),
            VirtualCalendar.AdultA or VirtualCalendar.AdultB => householdMetadata.FindDefaultPersonForCalendar(calendar) is { } person &&
                IsParticipantCalendarEvent(calendarEvent, person, householdMetadata),
            _ => false
        };

    private static bool IsParticipantCalendarEvent(
        CalendarEvent calendarEvent,
        Person person,
        IHouseholdMetadata householdMetadata) =>
        calendarEvent.PrimaryCalendar == householdMetadata.FindPrimaryCalendarFor(person) ||
        calendarEvent.Participants.Any(participant => participant.Person.Id == person.Id && participant.BusyStatus == BusyStatus.Busy);
}
