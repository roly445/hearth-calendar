using HearthCalendar.Client.Contracts.Ui;

namespace HearthCalendar.Client.Features.CalendarWorkspace;

public static class CalendarDisplay
{
    public static string FormatEvent(CalendarEventSummaryDto calendarEvent)
    {
        var time = calendarEvent.IsAllDay
            ? "All day"
            : $"{calendarEvent.StartTime:HH\\:mm} to {calendarEvent.EndTime:HH\\:mm}";

        return $"{time} - {calendarEvent.Category} - {string.Join(", ", calendarEvent.Participants)}";
    }
}
