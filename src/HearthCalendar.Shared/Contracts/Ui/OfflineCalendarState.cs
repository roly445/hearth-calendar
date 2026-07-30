namespace HearthCalendar.Shared.Contracts.Ui;

public sealed record OfflineCalendarSnapshot(
    IReadOnlyList<CalendarEventSummaryDto> UpcomingEvents,
    DateTimeOffset CachedAt);

public sealed record OfflineQueuedEventIntent(
    Guid LocalId,
    string RawText,
    DateOnly? Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    OfflineQueuedEventStatus Status,
    DateTimeOffset QueuedAt,
    DateTimeOffset? LastAttemptedAt = null,
    string? LastError = null);

public enum OfflineQueuedEventStatus
{
    PendingSync = 0,
    Syncing = 1,
    SyncFailed = 2
}

public static class OfflineCalendarState
{
    public static OfflineQueuedEventIntent QueueEventIntent(
        string rawText,
        DateOnly? date,
        TimeOnly? startTime,
        TimeOnly? endTime,
        DateTimeOffset queuedAt,
        Guid? localId = null) =>
        new(
            localId ?? Guid.NewGuid(),
            rawText,
            date,
            startTime,
            endTime,
            OfflineQueuedEventStatus.PendingSync,
            queuedAt);

    public static OfflineQueuedEventIntent MarkSyncing(
        OfflineQueuedEventIntent intent,
        DateTimeOffset attemptedAt) =>
        intent with
        {
            Status = OfflineQueuedEventStatus.Syncing,
            LastAttemptedAt = attemptedAt,
            LastError = null
        };

    public static OfflineQueuedEventIntent MarkSyncFailed(
        OfflineQueuedEventIntent intent,
        DateTimeOffset attemptedAt,
        string error) =>
        intent with
        {
            Status = OfflineQueuedEventStatus.SyncFailed,
            LastAttemptedAt = attemptedAt,
            LastError = error
        };

    public static IReadOnlyList<OfflineQueuedEventIntent> RemoveSynced(
        IReadOnlyList<OfflineQueuedEventIntent> intents,
        Guid localId) =>
        intents.Where(intent => intent.LocalId != localId).ToArray();
}
