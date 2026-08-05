using HearthCalendar.Client.Features.AdminSession;
using HearthCalendar.Client.Services;
using Microsoft.AspNetCore.Components;

namespace HearthCalendar.Client.Pages;

public partial class Login
{
    private readonly AdminLoginModel model = new();
    private bool isSubmitting;
    private string? message;

    [Inject]
    private AdminSessionClient SessionClient { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var session = await SessionClient.GetSessionAsync();
        if (session.IsAuthenticated)
        {
            Navigation.NavigateTo("/");
        }
    }

    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
        {
            message = "Enter your admin username and password.";
            return;
        }

        isSubmitting = true;
        var result = await SessionClient.LoginAsync(model.Username, model.Password);
        isSubmitting = false;

        if (result is null)
        {
            message = "Sign in failed. Check the details and try again.";
            return;
        }

        Navigation.NavigateTo("/");
    }
}
