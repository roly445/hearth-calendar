using Microsoft.AspNetCore.Identity;

namespace HearthCalendar.Server.Auth;

public sealed class HearthCalendarUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}
