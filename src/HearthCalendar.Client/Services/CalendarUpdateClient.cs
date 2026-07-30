using HearthCalendar.Shared.Contracts.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace HearthCalendar.Client.Services;

public sealed class CalendarUpdateClient(NavigationManager navigationManager) : IAsyncDisposable
{
    private HubConnection? connection;

    public event Func<CalendarUpdateNotification, Task>? CalendarUpdated;

    public async Task StartAsync()
    {
        if (connection is not null)
        {
            return;
        }

        connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/calendar-updates"))
            .WithAutomaticReconnect()
            .Build();
        connection.On<CalendarUpdateNotification>("CalendarUpdated", async notification =>
        {
            if (CalendarUpdated is not null)
            {
                await CalendarUpdated.Invoke(notification);
            }
        });

        await connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }
}
