using BluQube.Attributes;
using BluQube.Authorization;
using BluQube.Commands;
using BluQube.Queries;
using FluentValidation;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.CalDav;
using HearthCalendar.Server.Feeds;
using HearthCalendar.Server.Features.Credentials;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.SignalR;
using HearthCalendar.Client.Contracts.Ui;
using HearthCalendar.Server.Domain;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace HearthCalendar.Server;

[BluQubeResponder]
public class Program
{
    private const string ConfiguredOriginsPolicy = "ConfiguredOrigins";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseDefaultServiceProvider(ConfigureServiceProviderValidation);
        builder.WebHost.ConfigureKestrel(ConfigureKestrelServerOptions);
        builder.Services.AddHearthCalendarPersistence(builder.Configuration);
        builder.Services.AddHearthCalendarAuth(builder.Configuration);
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
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
            .AllowAnonymous();

        app.MapAdminAuthEndpoints();
        app.MapIntakeEndpoints();
        app.MapFeedEndpoints();
        app.MapCalDavEndpoints();
        app.MapHub<CalendarUpdatesHub>("/hubs/calendar-updates");
        app.AddBluQubeApi();

        app.MapFallbackToFile("index.html")
            .AllowAnonymous();

        app.Run();
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
                "script-src 'self' 'wasm-unsafe-eval'",
                "connect-src 'self'",
                "manifest-src 'self'",
                "worker-src 'self'",
                "form-action 'self'");

            await next();
        });
    }
}
