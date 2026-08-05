using HearthCalendar.Client.Contracts.Ui;

namespace HearthCalendar.Tests.Client.CalendarWorkspace.QueuedIntentDisplay;

public sealed class StatusTests
{
    [Theory]
    [InlineData(OfflineQueuedEventStatus.PendingSync, "Pending")]
    [InlineData(OfflineQueuedEventStatus.Syncing, "Syncing")]
    [InlineData(OfflineQueuedEventStatus.SyncFailed, "Retry")]
    public void Status_returns_user_visible_queue_status(
        OfflineQueuedEventStatus status,
        string expected)
    {
        var intent = new OfflineQueuedEventIntent(
            Guid.NewGuid(),
            "Family planning",
            null,
            null,
            null,
            status,
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
            null,
            null);

        var result = HearthCalendar.Client.Features.CalendarWorkspace.QueuedIntentDisplay.Status(intent);

        Assert.Equal(expected, result);
    }
}
