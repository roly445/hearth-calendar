using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Client.Features.CalendarWorkspace;

namespace HearthCalendar.Tests.Client.CalendarWorkspace.QueuedIntentDisplay;

public sealed class FormatIntentTests
{
    [Fact]
    public void FormatIntent_uses_waiting_state_when_no_error_exists()
    {
        var intent = QueuedIntent(OfflineQueuedEventStatus.PendingSync, null);

        var result = HearthCalendar.Client.Features.CalendarWorkspace.QueuedIntentDisplay.FormatIntent(intent);

        Assert.Equal("05 Aug - waiting to sync", result);
    }

    [Fact]
    public void FormatIntent_uses_last_error_when_present()
    {
        var intent = QueuedIntent(OfflineQueuedEventStatus.SyncFailed, "Sync will retry.");

        var result = HearthCalendar.Client.Features.CalendarWorkspace.QueuedIntentDisplay.FormatIntent(intent);

        Assert.Equal("05 Aug - Sync will retry.", result);
    }

    private static OfflineQueuedEventIntent QueuedIntent(
        OfflineQueuedEventStatus status,
        string? lastError) =>
        new(
            Guid.NewGuid(),
            "Family planning",
            new DateOnly(2026, 8, 5),
            null,
            null,
            status,
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
            null,
            lastError);
}
