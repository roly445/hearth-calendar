using HearthCalendar.Client.Contracts.Ui;

namespace HearthCalendar.Tests.Client;

public sealed class RemoveSyncedTests : OfflineCalendarStateTestBase
{
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
