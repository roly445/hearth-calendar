using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Features.CalendarWorkspace;

public partial class WorkspaceHeader
{
    [Parameter]
    public bool IsOffline { get; set; }

    [Parameter]
    public bool IsStale { get; set; }

    [Parameter]
    public bool IsLoading { get; set; }

    [Parameter]
    public EventCallback RefreshRequested { get; set; }
}
