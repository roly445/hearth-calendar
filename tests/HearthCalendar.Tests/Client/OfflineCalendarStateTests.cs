using HearthCalendar.Shared.Contracts.Ui;

namespace HearthCalendar.Tests.Client;

public sealed class OfflineCalendarStateTests
{
    [Fact]
    public void Queued_intent_starts_pending_and_does_not_create_an_approved_event()
    {
        var queuedAt = new DateTimeOffset(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);
        var localId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var intent = OfflineCalendarState.QueueEventIntent(
            "Shared meal",
            new DateOnly(2026, 8, 12),
            new TimeOnly(18, 0),
            new TimeOnly(19, 0),
            queuedAt,
            localId);

        Assert.Equal(localId, intent.LocalId);
        Assert.Equal(OfflineQueuedEventStatus.PendingSync, intent.Status);
        Assert.Equal("Shared meal", intent.RawText);
        Assert.Null(intent.LastAttemptedAt);
        Assert.Null(intent.LastError);
    }

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

    [Fact]
    public void Successful_sync_removes_only_the_synced_intent()
    {
        var first = OfflineCalendarState.QueueEventIntent(
            "Appointment",
            new DateOnly(2026, 8, 14),
            null,
            null,
            DateTimeOffset.UnixEpoch,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var second = OfflineCalendarState.QueueEventIntent(
            "Child A club",
            new DateOnly(2026, 8, 15),
            null,
            null,
            DateTimeOffset.UnixEpoch,
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        var remaining = OfflineCalendarState.RemoveSynced([first, second], first.LocalId);

        var only = Assert.Single(remaining);
        Assert.Equal(second.LocalId, only.LocalId);
    }
}
