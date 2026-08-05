using HearthCalendar.Client.Contracts.Ui;
using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Features.CalendarWorkspace;

public partial class QueuedIntentList
{
    [Parameter]
    public IReadOnlyList<OfflineQueuedEventIntent> QueuedIntents { get; set; } = [];
}
