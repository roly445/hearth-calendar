using HearthCalendar.Client.Contracts.Ui;

namespace HearthCalendar.Client.Features.CalendarWorkspace;

public static class QueuedIntentDisplay
{
    public static string Status(OfflineQueuedEventIntent intent) =>
        intent.Status switch
        {
            OfflineQueuedEventStatus.SyncFailed => "Retry",
            OfflineQueuedEventStatus.Syncing => "Syncing",
            _ => "Pending"
        };

    public static string FormatIntent(OfflineQueuedEventIntent intent)
    {
        var date = intent.Date?.ToString("dd MMM") ?? "No date";
        var state = intent.LastError is null ? "waiting to sync" : intent.LastError;

        return $"{date} - {state}";
    }
}
