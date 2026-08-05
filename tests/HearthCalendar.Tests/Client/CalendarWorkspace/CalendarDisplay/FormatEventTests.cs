using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Client.Features.CalendarWorkspace;

namespace HearthCalendar.Tests.Client.CalendarWorkspace.CalendarDisplay;

public sealed class FormatEventTests
{
    [Fact]
    public void FormatEvent_uses_all_day_label_for_all_day_events()
    {
        var calendarEvent = new CalendarEventSummaryDto(
            Guid.NewGuid(),
            "Family planning",
            new DateOnly(2026, 8, 5),
            null,
            null,
            true,
            "Family",
            "Family",
            "Busy",
            ["Adult A", "Adult B"]);

        var result = HearthCalendar.Client.Features.CalendarWorkspace.CalendarDisplay.FormatEvent(calendarEvent);

        Assert.Equal("All day - Family - Adult A, Adult B", result);
    }

    [Fact]
    public void FormatEvent_uses_time_range_for_timed_events()
    {
        var calendarEvent = new CalendarEventSummaryDto(
            Guid.NewGuid(),
            "Dentist",
            new DateOnly(2026, 8, 5),
            new TimeOnly(9, 0),
            new TimeOnly(9, 30),
            false,
            "AdultA",
            "Personal",
            "Busy",
            ["Adult A"]);

        var result = HearthCalendar.Client.Features.CalendarWorkspace.CalendarDisplay.FormatEvent(calendarEvent);

        Assert.Equal("09:00 to 09:30 - Personal - Adult A", result);
    }
}
