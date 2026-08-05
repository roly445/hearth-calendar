using HearthCalendar.Client.Contracts.Ui;
using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Features.CalendarWorkspace;

public partial class UpcomingEventsPanel
{
    [Parameter]
    public string? StatusMessage { get; set; }

    [Parameter]
    public IReadOnlyList<CalendarEventSummaryDto> Items { get; set; } = [];
}
