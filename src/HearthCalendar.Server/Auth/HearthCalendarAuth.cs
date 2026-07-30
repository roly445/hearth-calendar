using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace HearthCalendar.Server.Auth;

public static class HearthCalendarAuth
{
    public const string TokenScheme = "HearthCalendarToken";
    public const string AdminPolicy = "Admin";
    public const string IntakeWritePolicy = "IntakeWrite";
    public const string FeedReadPolicy = "FeedRead";
    public const string ScopeClaim = "scope";
    public const string TokenKindClaim = "token_kind";
    public const string AllowedCalendarClaim = "allowed_calendar";
    public const string ClientTokenKind = "client";
    public const string FeedTokenKind = "feed";
    public const string AdminWebScope = "admin:web";
    public const string IntakeWriteScope = "intake:write";
    public const string FeedReadScope = "feed:read";

    public static IServiceCollection AddHearthCalendarAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HearthCalendarAuthOptions>(configuration.GetSection("Auth"));
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddScheme<AuthenticationSchemeOptions, HearthCalendarTokenAuthenticationHandler>(
                TokenScheme,
                _ => { });

        services
            .AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(AdminPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(ScopeClaim, AdminWebScope);
            })
            .AddPolicy(IntakeWritePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(TokenScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(TokenKindClaim, ClientTokenKind);
                policy.RequireClaim(ScopeClaim, IntakeWriteScope);
            })
            .AddPolicy(FeedReadPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(TokenScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(TokenKindClaim, FeedTokenKind);
                policy.RequireClaim(ScopeClaim, FeedReadScope);
            });

        return services;
    }
}

public sealed record HearthCalendarAuthOptions
{
    public IReadOnlyList<ClientTokenOptions> ClientTokens { get; init; } = [];

    public IReadOnlyList<FeedTokenOptions> FeedTokens { get; init; } = [];
}

public sealed record ClientTokenOptions
{
    public required string Name { get; init; }

    public required string SecretHash { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];
}

public sealed record FeedTokenOptions
{
    public required string Name { get; init; }

    public required string TokenHash { get; init; }

    public IReadOnlyList<string> AllowedCalendars { get; init; } = [];

    public IReadOnlyList<string> Scopes { get; init; } = [];
}

public static class HearthCalendarSecretHasher
{
    private const string Prefix = "sha256:";

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

        return Prefix + Convert.ToBase64String(bytes);
    }

    public static bool Matches(string secret, string hash)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var candidate = Hash(secret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(hash));
    }
}

public sealed class HearthCalendarTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<HearthCalendarAuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadBearerToken();
        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var client = authOptions.Value.ClientTokens.FirstOrDefault(
            candidate => HearthCalendarSecretHasher.Matches(token, candidate.SecretHash));
        if (client is not null)
        {
            return Task.FromResult(Success(HearthCalendarTokenPrincipalFactory.Create(
                client.Name,
                HearthCalendarAuth.ClientTokenKind,
                client.Scopes,
                [])));
        }

        var feed = authOptions.Value.FeedTokens.FirstOrDefault(
            candidate => HearthCalendarSecretHasher.Matches(token, candidate.TokenHash));
        if (feed is not null)
        {
            return Task.FromResult(Success(HearthCalendarTokenPrincipalFactory.Create(
                feed.Name,
                HearthCalendarAuth.FeedTokenKind,
                feed.Scopes,
                feed.AllowedCalendars)));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid token."));
    }

    private string? ReadBearerToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[bearerPrefix.Length..].Trim();

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static AuthenticateResult Success(ClaimsPrincipal principal)
    {
        var ticket = new AuthenticationTicket(principal, HearthCalendarAuth.TokenScheme);

        return AuthenticateResult.Success(ticket);
    }
}

public static class HearthCalendarTokenPrincipalFactory
{
    public static ClaimsPrincipal Create(
        string name,
        string tokenKind,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> allowedCalendars)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, name),
            new(ClaimTypes.Name, name),
            new(HearthCalendarAuth.TokenKindClaim, tokenKind)
        };
        claims.AddRange(scopes.Select(scope => new Claim(HearthCalendarAuth.ScopeClaim, scope)));
        claims.AddRange(allowedCalendars.Select(calendar => new Claim(HearthCalendarAuth.AllowedCalendarClaim, calendar)));

        var identity = new ClaimsIdentity(claims, HearthCalendarAuth.TokenScheme);

        return new ClaimsPrincipal(identity);
    }
}
