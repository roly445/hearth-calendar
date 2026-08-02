using HearthCalendar.Client.Contracts.Ui;

namespace HearthCalendar.Tests.Client;

public sealed class MarkSyncFailedTests : OfflineCalendarStateTestBase
{
    [Fact]
    public void Sync_failure_keeps_intent_in_outbox_for_retry()
    {
        var queuedAt = new DateTimeOffset(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);
        var attemptedAt = queuedAt.AddMinutes(5);
        var intent = OfflineCalendarState.QueueEventIntent(
            "Planning meeting",
            new DateOnly(2026, 8, 13),
            new TimeOnly(16, 0),
            new TimeOnly(17, 0),
            queuedAt,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        var syncing = OfflineCalendarState.MarkSyncing(intent, attemptedAt);
        var failed = OfflineCalendarState.MarkSyncFailed(syncing, attemptedAt, "Check the event details and try again.");

        Assert.Equal(OfflineQueuedEventStatus.Syncing, syncing.Status);
        Assert.Equal(OfflineQueuedEventStatus.SyncFailed, failed.Status);
        Assert.Equal("Check the event details and try again.", failed.LastError);
        Assert.Equal(attemptedAt, failed.LastAttemptedAt);
    }
}
