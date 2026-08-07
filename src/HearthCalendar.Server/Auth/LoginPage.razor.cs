using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Server.Auth;

public partial class LoginPage
{
    [Parameter]
    public string? Message { get; set; }
}
