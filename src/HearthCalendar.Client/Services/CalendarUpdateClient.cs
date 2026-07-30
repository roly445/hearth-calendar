using HearthCalendar.Shared.Contracts.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace HearthCalendar.Client.Services;

public sealed class CalendarUpdateClient(NavigationManager navigationManager) : IAsyncDisposable
{
    private HubConnection? connection;

    public event Func<CalendarUpdateNotification, Task>? CalendarUpdated;
    public event Func<Task>? Reconnected;

    public async Task StartAsync()
    {
        if (connection?.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
        {
            return;
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }

        var nextConnection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/calendar-updates"))
            .WithAutomaticReconnect()
            .Build();
        nextConnection.On<CalendarUpdateNotification>("CalendarUpdated", async notification =>
        {
            if (CalendarUpdated is not null)
            {
                await CalendarUpdated.Invoke(notification);
            }
        });
        nextConnection.Reconnected += async _ =>
        {
            if (Reconnected is not null)
            {
                await Reconnected.Invoke();
            }
        };

        await nextConnection.StartAsync();
        connection = nextConnection;
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }
}
