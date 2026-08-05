using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using HearthCalendar.Client.Contracts.Auth;
using HearthCalendar.Server.Persistence;
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
    public const string CalDavReadPolicy = "CalDavRead";
    public const string CalDavWritePolicy = "CalDavWrite";
    public const string ScopeClaim = "scope";
    public const string TokenKindClaim = "token_kind";
    public const string AllowedCalendarClaim = "allowed_calendar";
    public const string CalDavReadableCalendarClaim = "caldav_readable_calendar";
    public const string CalDavWritableCalendarClaim = "caldav_writable_calendar";
    public const string ClientTokenKind = "client";
    public const string FeedTokenKind = "feed";
    public const string CalDavTokenKind = "caldav";
    public const string AdminWebScope = "admin:web";
    public const string IntakeWriteScope = "intake:write";
    public const string FeedReadScope = "feed:read";
    public const string CalDavReadScope = "caldav:read";
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
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
            .AddPolicy(CalDavReadPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(TokenScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(TokenKindClaim, CalDavTokenKind);
                policy.RequireAssertion(context =>
                    context.User.HasClaim(ScopeClaim, CalDavReadScope) ||
                    context.User.HasClaim(ScopeClaim, CalDavWriteScope));
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
    public IReadOnlyList<AdminUserOptions> AdminUsers { get; init; } = [];

    public IReadOnlyList<ClientTokenOptions> ClientTokens { get; init; } = [];

    public IReadOnlyList<FeedTokenOptions> FeedTokens { get; init; } = [];

    public IReadOnlyList<CalDavCredentialOptions> CalDavCredentials { get; init; } = [];
}

public sealed record AdminUserOptions
{
    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string PasswordHash { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];
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

    public IReadOnlyList<string> ReadableCalendars { get; init; } = [];

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

public static class HearthCalendarAdminPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return string.Join(
            ":",
            Prefix,
            DefaultIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool Matches(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split(':');
        if (parts is not [Prefix, var iterationText, var saltText, var hashText] ||
            !int.TryParse(iterationText, out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(saltText);
            expectedHash = Convert.FromBase64String(hashText);
        }
        catch (FormatException)
        {
            return false;
        }

        var candidateHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(candidateHash, expectedHash);
    }
}

public static class AdminAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/admin/login",
                async (AdminLoginRequest request, HttpContext context, IOptions<HearthCalendarAuthOptions> options) =>
                    await LoginAsync(request, context, options))
            .AllowAnonymous();
        endpoints.MapPost("/api/admin/logout", LogoutAsync)
            .RequireAuthorization(HearthCalendarAuth.AdminPolicy);
        endpoints.MapGet("/api/admin/session", (ClaimsPrincipal user) => Session(user))
            .RequireAuthorization(HearthCalendarAuth.AdminPolicy);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        AdminLoginRequest request,
        HttpContext context,
        IOptions<HearthCalendarAuthOptions> options)
    {
        var admin = options.Value.AdminUsers.FirstOrDefault(candidate =>
            string.Equals(candidate.Username, request.Username, StringComparison.Ordinal));
        if (admin is null || !HearthCalendarAdminPasswordHasher.Matches(request.Password, admin.PasswordHash))
        {
            return Results.Unauthorized();
        }

        var scopes = admin.Scopes.Count == 0 ? [HearthCalendarAuth.AdminWebScope] : admin.Scopes;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Username),
            new(ClaimTypes.Name, admin.DisplayName),
            new(HearthCalendarAuth.ScopeClaim, HearthCalendarAuth.AdminWebScope)
        };
        claims.AddRange(scopes
            .Where(scope => !string.Equals(scope, HearthCalendarAuth.AdminWebScope, StringComparison.Ordinal))
            .Select(scope => new Claim(HearthCalendarAuth.ScopeClaim, scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true
            });

        return Results.Ok(new AdminLoginResponse(admin.DisplayName));
    }

    private static async Task LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static IResult Session(ClaimsPrincipal user) =>
        Results.Ok(new AdminSessionResponse(true, user.FindFirstValue(ClaimTypes.Name)));
}

public sealed class HearthCalendarTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<HearthCalendarAuthOptions> authOptions,
    IHearthCalendarCredentialStore credentialStore)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string CalDavRealm = "Hearth Calendar CalDAV";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = ReadCredential();
        if (credential is null)
        {
            return AuthenticateResult.NoResult();
        }

        var client = authOptions.Value.ClientTokens.FirstOrDefault(
            candidate => credential.Kind == SubmittedCredentialKind.Bearer &&
                HearthCalendarSecretHasher.Matches(credential.Secret, candidate.SecretHash));
        if (client is not null)
        {
            return Success(HearthCalendarTokenPrincipalFactory.Create(
                client.Name,
                HearthCalendarAuth.ClientTokenKind,
                client.Scopes,
                []));
        }

        var feed = authOptions.Value.FeedTokens.FirstOrDefault(
            candidate => credential.Kind == SubmittedCredentialKind.Bearer &&
                HearthCalendarSecretHasher.Matches(credential.Secret, candidate.TokenHash));
        if (feed is not null)
        {
            return Success(HearthCalendarTokenPrincipalFactory.Create(
                feed.Name,
                HearthCalendarAuth.FeedTokenKind,
                feed.Scopes,
                feed.AllowedCalendars));
        }

        if (credential.Kind == SubmittedCredentialKind.Bearer)
        {
            var storedClient = await credentialStore.FindActiveClientCredentialAsync(
                credential.Secret,
                DateTimeOffset.UtcNow,
                Context.RequestAborted);
            if (storedClient is not null)
            {
                return Success(HearthCalendarTokenPrincipalFactory.Create(
                    storedClient.ClientName,
                    HearthCalendarAuth.ClientTokenKind,
                    storedClient.Scopes,
                    []));
            }
        }

        if (credential.Kind == SubmittedCredentialKind.Bearer)
        {
            var storedFeed = await credentialStore.FindActiveFeedTokenAsync(
                credential.Secret,
                DateTimeOffset.UtcNow,
                Context.RequestAborted);
            if (storedFeed is not null)
            {
                return Success(HearthCalendarTokenPrincipalFactory.Create(
                    storedFeed.Name,
                    HearthCalendarAuth.FeedTokenKind,
                    storedFeed.Scopes,
                    storedFeed.AllowedCalendars));
            }
        }

        var calDavCredential = authOptions.Value.CalDavCredentials.FirstOrDefault(
            candidate => credential.Kind == SubmittedCredentialKind.Basic &&
                string.Equals(candidate.Name, credential.Name, StringComparison.Ordinal) &&
                HearthCalendarSecretHasher.Matches(credential.Secret, candidate.SecretHash));
        if (calDavCredential is not null)
        {
            return Success(HearthCalendarTokenPrincipalFactory.Create(
                calDavCredential.Name,
                HearthCalendarAuth.CalDavTokenKind,
                calDavCredential.Scopes,
                [],
                calDavCredential.ReadableCalendars,
                calDavCredential.WritableCalendars));
        }

        if (credential.Kind == SubmittedCredentialKind.Basic && credential.Name is not null)
        {
            var storedCalDavCredential = await credentialStore.FindActiveCalDavCredentialAsync(
                credential.Name,
                credential.Secret,
                DateTimeOffset.UtcNow,
                Context.RequestAborted);
            if (storedCalDavCredential is not null)
            {
                return Success(HearthCalendarTokenPrincipalFactory.Create(
                    storedCalDavCredential.Name,
                    HearthCalendarAuth.CalDavTokenKind,
                    storedCalDavCredential.Scopes,
                    [],
                    storedCalDavCredential.ReadableCalendars,
                    storedCalDavCredential.WritableCalendars));
            }
        }

        return AuthenticateResult.Fail("Invalid token.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Request.Path.StartsWithSegments("/caldav", StringComparison.OrdinalIgnoreCase))
        {
            Response.Headers.WWWAuthenticate = $"Basic realm=\"{CalDavRealm}\"";
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;

        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
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
        IReadOnlyList<string> allowedCalendars,
        IReadOnlyList<string>? calDavReadableCalendars = null,
        IReadOnlyList<string>? calDavWritableCalendars = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, name),
            new(ClaimTypes.Name, name),
            new(HearthCalendarAuth.TokenKindClaim, tokenKind)
        };
        claims.AddRange(scopes.Select(scope => new Claim(HearthCalendarAuth.ScopeClaim, scope)));
        claims.AddRange(allowedCalendars.Select(calendar => new Claim(HearthCalendarAuth.AllowedCalendarClaim, calendar)));
        claims.AddRange((calDavReadableCalendars ?? []).Select(calendar =>
            new Claim(HearthCalendarAuth.CalDavReadableCalendarClaim, calendar)));
        claims.AddRange((calDavWritableCalendars ?? []).Select(calendar =>
            new Claim(HearthCalendarAuth.CalDavWritableCalendarClaim, calendar)));

        var identity = new ClaimsIdentity(claims, HearthCalendarAuth.TokenScheme);

        return new ClaimsPrincipal(identity);
    }
}
