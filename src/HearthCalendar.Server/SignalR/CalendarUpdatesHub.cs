using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HearthCalendar.Server.SignalR;

[Authorize(Policy = Auth.HearthCalendarAuth.AdminPolicy)]
public sealed class CalendarUpdatesHub : Hub;
