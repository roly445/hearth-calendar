using HearthCalendar.Client.Contracts.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HearthCalendar.Client.Features.CalendarWorkspace;

public partial class CreateEventPanel
{
    [Parameter, EditorRequired]
    public CreateEventModel Model { get; set; } = null!;

    [Parameter]
    public bool IsSubmitting { get; set; }

    [Parameter]
    public IReadOnlyList<OfflineQueuedEventIntent> QueuedIntents { get; set; } = [];

    [Parameter]
    public EventCallback Submitted { get; set; }

    [Parameter]
    public EventCallback<ChangeEventArgs> DateChanged { get; set; }

    [Parameter]
    public EventCallback<ChangeEventArgs> StartTimeChanged { get; set; }

    [Parameter]
    public EventCallback<ChangeEventArgs> EndTimeChanged { get; set; }
}
