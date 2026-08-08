using BluQube.Attributes;
using BluQube.Authorization;
using BluQube.Commands;
using BluQube.Queries;
using FluentValidation;
using System.Text.RegularExpressions;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.CalDav;
using HearthCalendar.Server.Feeds;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.SignalR;
using HearthCalendar.Server.Testing;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace HearthCalendar.Server;

[BluQubeResponder]
public class Program
{
    private const string ConfiguredOriginsPolicy = "ConfiguredOrigins";

    public static int Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ConfigureLocalDevelopmentConfiguration(builder.Configuration, builder.Environment);
        builder.Host.UseDefaultServiceProvider(ConfigureServiceProviderValidation);
        builder.WebHost.ConfigureKestrel(ConfigureKestrelServerOptions);
        builder.Services.AddHearthCalendarPersistence(builder.Configuration);
        ConfigureLocalDevelopmentDataProtection(builder.Services, builder.Environment);
        builder.Services.AddHearthCalendarAuth(builder.Configuration);
        builder.Services.AddBrowserTestServices(builder.Configuration, builder.Environment);
        builder.Services.AddRazorComponents();
        builder.Services.Configure<HearthCalendarSecurityOptions>(builder.Configuration.GetSection("Security"));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IAiReviewProvider>(_ => NoOpAiReviewProvider.Instance);
        builder.Services.AddScoped<ICalendarUpdateNotifier, SignalRCalendarUpdateNotifier>();
        builder.Services.AddScoped<IValidator<SubmitWebEventIntentCommand>, SubmitWebEventIntentCommandValidator>();
        builder.Services.AddScoped<IValidator<EditReviewItemCommand>, EditReviewItemCommandValidator>();
        builder.Services.AddScoped<IValidator<DeleteEventCommand>, DeleteEventCommandValidator>();
        builder.Services.AddScoped<IValidator<RescheduleEventCommand>, RescheduleEventCommandValidator>();
        builder.Services.AddBluQube(typeof(Program).Assembly);
        builder.Services.AddBluQubeAuthorization(typeof(Program).Assembly, options =>
        {
            options.RequireAuthorizationByDefault = true;
        });
        builder.Services.AddScoped<ICommandRunner, CommandRunner>();
        builder.Services.AddScoped<IQueryRunner, QueryRunner>();
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.AddBluQubeJsonConverters();
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(ConfiguredOriginsPolicy, policy =>
            {
                var security = builder.Configuration
                    .GetSection("Security")
                    .Get<HearthCalendarSecurityOptions>() ?? new HearthCalendarSecurityOptions();
                var allowedOrigins = security.Cors.AllowedOrigins;

                if (allowedOrigins.Count > 0)
                {
                    policy
                        .WithOrigins([.. allowedOrigins])
                        .AllowCredentials()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                }
                else
                {
                    policy.SetIsOriginAllowed(_ => false);
                }
            });
        });

        var app = builder.Build();

        app.UseSecurityHeaders();
        app.UseCors(ConfiguredOriginsPolicy);
        app.UseAdminIdentityBootstrap();
        app.UseAuthentication();
        app.UseBrowserTestAuthentication();
        app.UseAppShellAuthenticationGate();
        app.UseBrowserTestStaticFiles();
        if (!app.IsBrowserTestSeedDataEnabled())
        {
            app.UseBlazorFrameworkFiles();
        }
        app.UseStaticFiles();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
            .AllowAnonymous();

        app.MapAdminAuthEndpoints();
        app.MapIntakeEndpoints();
        app.MapFeedEndpoints();
        app.MapCalDavEndpoints();
        app.MapHub<CalendarUpdatesHub>("/hubs/calendar-updates");
        app.AddBluQubeApi();
        app.MapClientRootFingerprintAssets();
        app.MapBrowserTestAppShell();

        app.MapFallbackToFile("/index.html")
            .RequireAuthorization(HearthCalendarAuth.AdminPolicy);

        app.Run();

        return 0;
    }

    public static void ConfigureServiceProviderValidation(
        HostBuilderContext context,
        ServiceProviderOptions options)
    {
        if (!ShouldValidateServiceProvider(context.HostingEnvironment.EnvironmentName))
        {
            return;
        }

        options.ValidateOnBuild = true;
        options.ValidateScopes = true;
    }

    public static void ConfigureKestrelServerOptions(KestrelServerOptions options)
    {
        options.AddServerHeader = false;
    }

    public static void ConfigureLocalDevelopmentConfiguration(
        ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        configuration.AddJsonFile(
            "appsettings.Local.json",
            optional: true,
            reloadOnChange: true);
    }

    public static void ConfigureLocalDevelopmentDataProtection(
        IServiceCollection services,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Test"))
        {
            return;
        }

        var keyDirectory = new DirectoryInfo(Path.Combine(
            environment.ContentRootPath,
            ".local",
            "data-protection-keys"));
        keyDirectory.Create();
        services
            .AddDataProtection()
            .PersistKeysToFileSystem(keyDirectory);
    }

    public static bool ShouldValidateServiceProvider(string environmentName)
    {
        return string.Equals(environmentName, Environments.Development, StringComparison.Ordinal)
            || string.Equals(environmentName, "Test", StringComparison.Ordinal);
    }
}

public sealed record HealthResponse(string Status);

public sealed record HearthCalendarSecurityOptions
{
    public HearthCalendarCorsOptions Cors { get; init; } = new();
}

public sealed record HearthCalendarCorsOptions
{
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}

internal static class SecurityHeadersMiddleware
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = string.Join(
                ", ",
                "accelerometer=()",
                "camera=()",
                "geolocation=()",
                "gyroscope=()",
                "magnetometer=()",
                "microphone=()",
                "payment=()",
                "usb=()");
            headers.ContentSecurityPolicy = string.Join(
                "; ",
                "default-src 'self'",
                "base-uri 'self'",
                "object-src 'none'",
                "frame-ancestors 'none'",
                "img-src 'self' data: blob:",
                "font-src 'self'",
                "style-src 'self'",
                "script-src 'self' 'wasm-unsafe-eval' 'unsafe-inline'",
                "connect-src 'self'",
                "manifest-src 'self'",
                "worker-src 'self'",
                "form-action 'self'");

            await next();
        });
    }
}

internal static class AppShellAuthenticationGateMiddleware
{
    public static IApplicationBuilder UseAppShellAuthenticationGate(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!IsProtectedAppShellNavigation(context.Request))
            {
                await next();
                return;
            }

            var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
            var result = await authorization.AuthorizeAsync(
                context.User,
                null,
                HearthCalendarAuth.AdminPolicy);

            if (result.Succeeded)
            {
                await next();
                return;
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                await context.ForbidAsync(IdentityConstants.ApplicationScheme);
                return;
            }

            await context.ChallengeAsync(IdentityConstants.ApplicationScheme);
        });
    }

    private static bool IsProtectedAppShellNavigation(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (!AcceptsHtml(request))
        {
            return false;
        }

        if (IsAnonymousPath(request.Path))
        {
            return false;
        }

        return !Path.HasExtension(request.Path);
    }

    private static bool AcceptsHtml(HttpRequest request) =>
        request.Headers.Accept.Count == 0 ||
        request.Headers.Accept.Any(value =>
            value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsAnonymousPath(PathString path) =>
        path.StartsWithSegments("/login", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/caldav", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/feeds", StringComparison.OrdinalIgnoreCase);
}

internal static partial class ClientRootFingerprintAssets
{
    public static IEndpointRouteBuilder MapClientRootFingerprintAssets(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/{fileName:regex(^offline-calendar\\.[a-z0-9]+\\.js$)}",
                ClientRootFingerprintAssetAsync)
            .AllowAnonymous();

        return endpoints;
    }

    private static IResult ClientRootFingerprintAssetAsync(string fileName, IHostEnvironment environment)
    {
        var sourceName = FingerprintedRootAssetRegex()
            .Replace(fileName, "${name}.${extension}");
        if (string.Equals(sourceName, fileName, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var clientRoot = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            "..",
            "HearthCalendar.Client"));
        var sourcePath = Path.Combine(clientRoot, "wwwroot", sourceName);

        return File.Exists(sourcePath)
            ? Results.File(sourcePath, "text/javascript; charset=utf-8")
            : Results.NotFound();
    }

    [GeneratedRegex("^(?<name>offline-calendar)\\.[a-z0-9]+\\.(?<extension>js)$", RegexOptions.IgnoreCase)]
    private static partial Regex FingerprintedRootAssetRegex();
}
