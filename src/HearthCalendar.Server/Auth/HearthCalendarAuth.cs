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
    public const string CalDavWritePolicy = "CalDavWrite";
    public const string ScopeClaim = "scope";
    public const string TokenKindClaim = "token_kind";
    public const string AllowedCalendarClaim = "allowed_calendar";
    public const string ClientTokenKind = "client";
    public const string FeedTokenKind = "feed";
    public const string CalDavTokenKind = "caldav";
    public const string AdminWebScope = "admin:web";
    public const string IntakeWriteScope = "intake:write";
    public const string FeedReadScope = "feed:read";
    public const string CalDavWriteScope = "caldav:write";

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
            })
            .AddPolicy(CalDavWritePolicy, policy =>
            {
                policy.AddAuthenticationSchemes(TokenScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(TokenKindClaim, CalDavTokenKind);
                policy.RequireClaim(ScopeClaim, CalDavWriteScope);
            });

        return services;
    }
}

public sealed record HearthCalendarAuthOptions
{
    public IReadOnlyList<ClientTokenOptions> ClientTokens { get; init; } = [];

    public IReadOnlyList<FeedTokenOptions> FeedTokens { get; init; } = [];

    public IReadOnlyList<CalDavCredentialOptions> CalDavCredentials { get; init; } = [];
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

public sealed record CalDavCredentialOptions
{
    public required string Name { get; init; }

    public required string SecretHash { get; init; }

    public IReadOnlyList<string> WritableCalendars { get; init; } = [];

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
        var credential = ReadCredential();
        if (credential is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var client = authOptions.Value.ClientTokens.FirstOrDefault(
            candidate => credential.Kind == SubmittedCredentialKind.Bearer &&
                HearthCalendarSecretHasher.Matches(credential.Secret, candidate.SecretHash));
        if (client is not null)
        {
            return Task.FromResult(Success(HearthCalendarTokenPrincipalFactory.Create(
                client.Name,
                HearthCalendarAuth.ClientTokenKind,
                client.Scopes,
                [])));
        }

        var feed = authOptions.Value.FeedTokens.FirstOrDefault(
            candidate => credential.Kind == SubmittedCredentialKind.Bearer &&
                HearthCalendarSecretHasher.Matches(credential.Secret, candidate.TokenHash));
        if (feed is not null)
        {
            return Task.FromResult(Success(HearthCalendarTokenPrincipalFactory.Create(
                feed.Name,
                HearthCalendarAuth.FeedTokenKind,
                feed.Scopes,
                feed.AllowedCalendars)));
        }

        var calDavCredential = authOptions.Value.CalDavCredentials.FirstOrDefault(
            candidate => credential.Kind == SubmittedCredentialKind.Basic &&
                string.Equals(candidate.Name, credential.Name, StringComparison.Ordinal) &&
                HearthCalendarSecretHasher.Matches(credential.Secret, candidate.SecretHash));
        if (calDavCredential is not null)
        {
            return Task.FromResult(Success(HearthCalendarTokenPrincipalFactory.Create(
                calDavCredential.Name,
                HearthCalendarAuth.CalDavTokenKind,
                calDavCredential.Scopes,
                calDavCredential.WritableCalendars)));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid token."));
    }

    private SubmittedCredential? ReadCredential()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            var feedQueryToken = ReadFeedQueryToken();

            return feedQueryToken is null
                ? null
                : new SubmittedCredential(SubmittedCredentialKind.Bearer, null, feedQueryToken);
        }

        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ReadBasicCredential(authorization);
        }

        var token = authorization[bearerPrefix.Length..].Trim();

        return string.IsNullOrWhiteSpace(token)
            ? null
            : new SubmittedCredential(SubmittedCredentialKind.Bearer, null, token);
    }

    private static SubmittedCredential? ReadBasicCredential(string authorization)
    {
        const string basicPrefix = "Basic ";
        if (!authorization.StartsWith(basicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var encoded = authorization[basicPrefix.Length..].Trim();
        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var name = decoded[..separator];
        var secret = decoded[(separator + 1)..];

        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(secret)
            ? null
            : new SubmittedCredential(SubmittedCredentialKind.Basic, name, secret);
    }

    private string? ReadFeedQueryToken()
    {
        if (!Request.Path.StartsWithSegments("/feeds", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = Request.Query["token"].ToString();

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private sealed record SubmittedCredential(SubmittedCredentialKind Kind, string? Name, string Secret);

    private enum SubmittedCredentialKind
    {
        Bearer,
        Basic
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
