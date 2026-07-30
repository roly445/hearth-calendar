using BluQube.Attributes;
using BluQube.Authorization;
using BluQube.Commands;
using BluQube.Queries;
using FluentValidation;
using HearthCalendar.Server.Auth;
using HearthCalendar.Server.Features.Ui;
using HearthCalendar.Server.Intake;
using HearthCalendar.Server.Persistence;
using HearthCalendar.Server.SignalR;
using HearthCalendar.Shared.Contracts.Ui;
using Microsoft.AspNetCore.Http.Json;

namespace HearthCalendar.Server;

[BluQubeResponder]
public class Program
{
    private const string ConfiguredOriginsPolicy = "ConfiguredOrigins";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseDefaultServiceProvider(ConfigureServiceProviderValidation);
        builder.Services.AddHearthCalendarPersistence(builder.Configuration);
        builder.Services.AddHearthCalendarAuth(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSignalR();
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
                var allowedOrigins = builder.Configuration
                    .GetSection("Security:Cors:AllowedOrigins")
                    .Get<string[]>() ?? [];

                if (allowedOrigins.Length > 0)
                {
                    policy
                        .WithOrigins(allowedOrigins)
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

        app.MapGet("/api/admin/session", () => Results.Ok(new AdminSessionResponse("authenticated")))
            .RequireAuthorization(HearthCalendarAuth.AdminPolicy);
        app.MapIntakeEndpoints();
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

    public static bool ShouldValidateServiceProvider(string environmentName)
    {
        return string.Equals(environmentName, Environments.Development, StringComparison.Ordinal)
            || string.Equals(environmentName, "Test", StringComparison.Ordinal);
    }
}

public sealed record HealthResponse(string Status);

public sealed record AdminSessionResponse(string Status);

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
