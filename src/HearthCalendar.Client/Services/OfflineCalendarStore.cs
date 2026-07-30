using System.Text.Json;
using HearthCalendar.Shared.Contracts.Ui;
using Microsoft.JSInterop;

namespace HearthCalendar.Client.Services;

public sealed class OfflineCalendarStore(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private const string SnapshotKey = "hearth-calendar:offline:upcoming-snapshot";
    private const string OutboxKey = "hearth-calendar:offline:event-intent-outbox";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private IJSObjectReference? module;
    private DotNetObjectReference<OfflineCalendarStore>? dotNetReference;

    public event Func<Task>? BrowserCameOnline;

    public async ValueTask StartOnlineListenerAsync()
    {
        dotNetReference ??= DotNetObjectReference.Create(this);
        await (await ModuleAsync()).InvokeVoidAsync("startOnlineListener", dotNetReference);
    }

    public async ValueTask<bool> IsOnlineAsync() =>
        await (await ModuleAsync()).InvokeAsync<bool>("isOnline");

    public async ValueTask<OfflineCalendarSnapshot?> ReadSnapshotAsync()
    {
        var json = await ReadJsonAsync(SnapshotKey);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<OfflineCalendarSnapshot>(json, JsonOptions);
    }

    public async ValueTask StoreSnapshotAsync(IReadOnlyList<CalendarEventSummaryDto> upcomingEvents, DateTimeOffset cachedAt)
    {
        var snapshot = new OfflineCalendarSnapshot(upcomingEvents, cachedAt);
        await WriteJsonAsync(SnapshotKey, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public async ValueTask<IReadOnlyList<OfflineQueuedEventIntent>> ReadOutboxAsync()
    {
        var json = await ReadJsonAsync(OutboxKey);
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<OfflineQueuedEventIntent[]>(json, JsonOptions) ?? [];
    }

    public async ValueTask<OfflineQueuedEventIntent> QueueEventIntentAsync(
        string rawText,
        DateOnly? date,
        TimeOnly? startTime,
        TimeOnly? endTime,
        DateTimeOffset queuedAt)
    {
        var intent = OfflineCalendarState.QueueEventIntent(rawText, date, startTime, endTime, queuedAt);
        var outbox = (await ReadOutboxAsync()).Append(intent).ToArray();
        await StoreOutboxAsync(outbox);
        return intent;
    }

    public ValueTask StoreOutboxAsync(IReadOnlyList<OfflineQueuedEventIntent> outbox) =>
        WriteJsonAsync(OutboxKey, JsonSerializer.Serialize(outbox, JsonOptions));

    private async ValueTask<string?> ReadJsonAsync(string key) =>
        await (await ModuleAsync()).InvokeAsync<string?>("readItem", key);

    private async ValueTask WriteJsonAsync(string key, string value) =>
        await (await ModuleAsync()).InvokeVoidAsync("writeItem", key, value);

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./offline-calendar.js");

    [JSInvokable]
    public async Task NotifyBrowserOnlineAsync()
    {
        if (BrowserCameOnline is not null)
        {
            await BrowserCameOnline.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            await module.InvokeVoidAsync("stopOnlineListener");
            await module.DisposeAsync();
        }

        dotNetReference?.Dispose();
    }
}
