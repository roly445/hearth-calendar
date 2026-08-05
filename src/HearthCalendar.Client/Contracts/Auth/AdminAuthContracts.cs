namespace HearthCalendar.Client.Contracts.Auth;

public sealed record AdminLoginRequest(string Username, string Password);

public sealed record AdminLoginResponse(string DisplayName);

public sealed record AdminSessionResponse(bool IsAuthenticated, string? DisplayName = null);
