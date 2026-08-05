using BluQube.Commands;
using BluQube.Constants;
using BluQube.Queries;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Client.Features.CalendarWorkspace;
using HearthCalendar.Client.Services;
using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Pages;

public partial class Home : IAsyncDisposable
{
    private readonly CreateEventModel createModel = new();
    private IReadOnlyList<ReviewQueueItemDto> reviewItems = [];
    private IReadOnlyList<CalendarEventSummaryDto> upcomingEvents = [];
    private IReadOnlyList<OfflineQueuedEventIntent> queuedIntents = [];
    private string? reviewStatusMessage;
    private string? eventsStatusMessage;
    private bool isLoading;
    private bool isSubmitting;
    private bool isOffline;
    private bool isStale;
    private string? message;
    private bool CanUseOnlineActions => !isOffline && !isStale && !isLoading;

    [Inject]
    private ICommandRunner CommandRunner { get; set; } = null!;

    [Inject]
    private IQueryRunner QueryRunner { get; set; } = null!;

    [Inject]
    private CalendarUpdateClient Updates { get; set; } = null!;

    [Inject]
    private OfflineCalendarStore OfflineStore { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        Updates.CalendarUpdated += OnCalendarUpdatedAsync;
        Updates.Reconnected += OnReconnectedAsync;
        OfflineStore.BrowserCameOnline += OnBrowserOnlineAsync;
        await OfflineStore.StartOnlineListenerAsync();
        await LoadOfflineStateAsync();
        await StartNotificationsAsync();
        await RefreshAsync();
    }

    private async Task StartNotificationsAsync()
    {
        try
        {
            await Updates.StartAsync();
        }
        catch (Exception)
        {
            message = "Live updates are unavailable for this session.";
        }
    }

    private async Task OnCalendarUpdatedAsync(CalendarUpdateNotification notification)
    {
        await RefreshAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnReconnectedAsync()
    {
        await RefreshAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnBrowserOnlineAsync()
    {
        await StartNotificationsAsync();
        await RefreshAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshAsync()
    {
        isLoading = true;
        queuedIntents = await OfflineStore.ReadOutboxAsync();
        isOffline = !await IsOnlineAsync();

        if (isOffline)
        {
            await LoadOfflineStateAsync();
            reviewItems = [];
            reviewStatusMessage = "Review actions need a fresh online connection.";
            eventsStatusMessage ??= "Upcoming events are unavailable offline until an online snapshot has been cached.";
            isLoading = false;
            return;
        }

        isStale = false;
        await SyncQueuedIntentsAsync();
        queuedIntents = await OfflineStore.ReadOutboxAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var reviewResult = await QueryRunner.Send(new GetReviewQueueQuery());
        var eventResult = await QueryRunner.Send(new GetUpcomingEventsQuery(today, today.AddDays(60)));

        (reviewItems, reviewStatusMessage) = ReadCollectionResult(
            reviewResult.Status,
            reviewResult.Status == QueryResultStatus.Succeeded ? reviewResult.Data.Items : [],
            "No staged items.",
            "Review queue is unavailable.");
        (upcomingEvents, eventsStatusMessage) = ReadCollectionResult(
            eventResult.Status,
            eventResult.Status == QueryResultStatus.Succeeded ? eventResult.Data.Items : [],
            "No approved events in the next 60 days.",
            "Upcoming events are unavailable.");

        if (eventResult.Status is QueryResultStatus.Succeeded or QueryResultStatus.Empty)
        {
            await OfflineStore.StoreSnapshotAsync(upcomingEvents, DateTimeOffset.Now);
        }

        isLoading = false;
    }

    private async Task SubmitEventAsync()
    {
        if (string.IsNullOrWhiteSpace(createModel.RawText))
        {
            message = "Add event details first.";
            return;
        }

        if (!await IsOnlineAsync())
        {
            await OfflineStore.QueueEventIntentAsync(
                createModel.RawText,
                ParseDate(createModel.DateText),
                ParseTime(createModel.StartTimeText),
                ParseTime(createModel.EndTimeText),
                DateTimeOffset.Now);
            ClearCreateForm();
            queuedIntents = await OfflineStore.ReadOutboxAsync();
            isOffline = true;
            message = "Event queued offline. It will sync for review when the app reconnects.";
            return;
        }

        await RunCommandAsync(new SubmitWebEventIntentCommand(
            createModel.RawText,
            ParseDate(createModel.DateText),
            ParseTime(createModel.StartTimeText),
            ParseTime(createModel.EndTimeText)));
        ClearCreateForm();
    }

    private void ClearCreateForm()
    {
        createModel.RawText = "";
        createModel.DateText = "";
        createModel.StartTimeText = "";
        createModel.EndTimeText = "";
    }

    private Task ApproveAsync(ReviewQueueItemDto item) =>
        RunCommandAsync(new ApproveReviewItemCommand(item.ReviewDecisionId));

    private Task RejectAsync(ReviewQueueItemDto item) =>
        RunCommandAsync(new RejectReviewItemCommand(item.ReviewDecisionId));

    private Task EditAsync(ReviewQueueEditRequest request) =>
        RunCommandAsync(new EditReviewItemCommand(
            request.Item.ReviewDecisionId,
            request.RawText,
            request.Item.Candidate?.Date,
            request.Item.Candidate?.StartTime,
            request.Item.Candidate?.EndTime));

    private async Task RunCommandAsync<TResult>(ICommand<TResult> command)
        where TResult : ICommandResult, IReviewActionResult
    {
        if (!await IsOnlineAsync())
        {
            isOffline = true;
            message = "This action needs a fresh online connection.";
            return;
        }

        isSubmitting = true;
        var result = await CommandRunner.Send(command);
        message = result.Status == CommandResultStatus.Succeeded
            ? result.Data.Message
            : CommandMessage(result.Status);
        isSubmitting = false;

        await RefreshAsync();
    }

    private async Task SyncQueuedIntentsAsync()
    {
        var outbox = (await OfflineStore.ReadOutboxAsync()).ToArray();

        foreach (var queued in outbox.ToArray())
        {
            var attemptedAt = DateTimeOffset.Now;
            outbox = ReplaceQueuedIntent(outbox, OfflineCalendarState.MarkSyncing(queued, attemptedAt));
            await OfflineStore.StoreOutboxAsync(outbox);

            try
            {
                var result = await CommandRunner.Send(new SubmitWebEventIntentCommand(
                    queued.RawText,
                    queued.Date,
                    queued.StartTime,
                    queued.EndTime));

                if (result.Status == CommandResultStatus.Succeeded)
                {
                    outbox = OfflineCalendarState.RemoveSynced(outbox, queued.LocalId).ToArray();
                    message = "Queued event synced and is waiting for server review.";
                }
                else
                {
                    outbox = ReplaceQueuedIntent(
                        outbox,
                        OfflineCalendarState.MarkSyncFailed(queued, attemptedAt, CommandMessage(result.Status)));
                }
            }
            catch (Exception)
            {
                outbox = ReplaceQueuedIntent(
                    outbox,
                    OfflineCalendarState.MarkSyncFailed(queued, attemptedAt, "Sync will retry when the connection is stable."));
            }

            await OfflineStore.StoreOutboxAsync(outbox);
        }
    }

    private async Task LoadOfflineStateAsync()
    {
        queuedIntents = await OfflineStore.ReadOutboxAsync();
        var snapshot = await OfflineStore.ReadSnapshotAsync();

        if (snapshot is null)
        {
            isStale = false;
            upcomingEvents = [];
            return;
        }

        isStale = true;
        upcomingEvents = snapshot.UpcomingEvents;
        eventsStatusMessage = $"Showing stale upcoming events cached {snapshot.CachedAt.LocalDateTime:g}.";
    }

    private async Task<bool> IsOnlineAsync()
    {
        try
        {
            return await OfflineStore.IsOnlineAsync();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static OfflineQueuedEventIntent[] ReplaceQueuedIntent(
        IReadOnlyList<OfflineQueuedEventIntent> outbox,
        OfflineQueuedEventIntent replacement) =>
        outbox
            .Select(intent => intent.LocalId == replacement.LocalId ? replacement : intent)
            .ToArray();

    private void SetDate(ChangeEventArgs args) =>
        createModel.DateText = args.Value?.ToString() ?? "";

    private void SetStartTime(ChangeEventArgs args) =>
        createModel.StartTimeText = args.Value?.ToString() ?? "";

    private void SetEndTime(ChangeEventArgs args) =>
        createModel.EndTimeText = args.Value?.ToString() ?? "";

    private static (IReadOnlyList<T> Items, string? Message) ReadCollectionResult<T>(
        QueryResultStatus status,
        IReadOnlyList<T> items,
        string emptyMessage,
        string failedMessage) =>
        status switch
        {
            QueryResultStatus.Succeeded => (items, null),
            QueryResultStatus.Empty => ([], emptyMessage),
            QueryResultStatus.Unauthorized => ([], "Sign in with admin access to view this data."),
            _ => ([], failedMessage)
        };

    private static string CommandMessage(CommandResultStatus status) =>
        status switch
        {
            CommandResultStatus.Invalid => "Check the event details and try again.",
            CommandResultStatus.Failed => "The action was rejected by the server.",
            _ => "The action could not be completed."
        };

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var date) ? date : null;

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParse(value, out var time) ? time : null;

    public ValueTask DisposeAsync()
    {
        Updates.CalendarUpdated -= OnCalendarUpdatedAsync;
        Updates.Reconnected -= OnReconnectedAsync;
        OfflineStore.BrowserCameOnline -= OnBrowserOnlineAsync;
        return Updates.DisposeAsync();
    }
}
