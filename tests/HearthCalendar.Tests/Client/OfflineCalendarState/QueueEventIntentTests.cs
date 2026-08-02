using HearthCalendar.Client.Contracts.Ui;

namespace HearthCalendar.Tests.Client;

public sealed class QueueEventIntentTests : OfflineCalendarStateTestBase
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
}
