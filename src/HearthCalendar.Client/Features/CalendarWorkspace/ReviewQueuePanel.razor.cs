using HearthCalendar.Client.Contracts.Ui;
using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Features.CalendarWorkspace;

public partial class ReviewQueuePanel
{
    private readonly Dictionary<Guid, string> editText = [];

    [Parameter]
    public string? StatusMessage { get; set; }

    [Parameter]
    public IReadOnlyList<ReviewQueueItemDto> Items { get; set; } = [];

    [Parameter]
    public bool CanUseOnlineActions { get; set; }

    [Parameter]
    public bool IsSubmitting { get; set; }

    [Parameter]
    public EventCallback<ReviewQueueItemDto> ApproveRequested { get; set; }

    [Parameter]
    public EventCallback<ReviewQueueItemDto> RejectRequested { get; set; }

    [Parameter]
    public EventCallback<ReviewQueueEditRequest> EditRequested { get; set; }

    protected override void OnParametersSet()
    {
        foreach (var item in Items)
        {
            editText.TryAdd(item.ReviewDecisionId, item.RawText);
        }
    }

    private string GetEditText(ReviewQueueItemDto item) =>
        editText.TryGetValue(item.ReviewDecisionId, out var text) ? text : item.RawText;

    private void SetEditText(ReviewQueueItemDto item, ChangeEventArgs args) =>
        editText[item.ReviewDecisionId] = args.Value?.ToString() ?? "";

    private Task EditAsync(ReviewQueueItemDto item) =>
        EditRequested.InvokeAsync(new ReviewQueueEditRequest(item, GetEditText(item)));
}

public sealed record ReviewQueueEditRequest(ReviewQueueItemDto Item, string RawText);
