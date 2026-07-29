using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HearthCalendar.Server;

public class Program
{
    private const string ConfiguredOriginsPolicy = "ConfiguredOrigins";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseDefaultServiceProvider(ConfigureServiceProviderValidation);

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie();

        builder.Services
            .AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

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
