using HearthCalendar.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

namespace HearthCalendar.Tests.Server;

public sealed class HealthEndpointProgramTests : HealthEndpointTestBase
{
    [Fact]
    public async Task HealthEndpointMatchesSnapshot()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        var endpoint = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Single(endpoint => endpoint.DisplayName?.Contains("HTTP: GET /health", StringComparison.Ordinal) == true);

        await Verifier.Verify(new
        {
            StatusCode = response.StatusCode,
            Body = content,
            IsExplicitlyAnonymous = endpoint.Metadata.Any(metadata => metadata is IAllowAnonymous),
            SecurityHeaders = new
            {
                XContentTypeOptions = response.Headers.GetValues("X-Content-Type-Options").Single(),
                XFrameOptions = response.Headers.GetValues("X-Frame-Options").Single(),
                ReferrerPolicy = response.Headers.GetValues("Referrer-Policy").Single(),
                PermissionsPolicy = response.Headers.GetValues("Permissions-Policy").Single(),
                ContentSecurityPolicy = response.Headers.GetValues("Content-Security-Policy").Single()
            }
        });
    }

    [Fact]
    public async Task ServerDefaultsMatchSnapshot()
    {
        await using var factory = CreateFactory();
        _ = factory.CreateClient();

        var authorizationOptions = factory.Services
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value;

        await Verifier.Verify(new
        {
            HasFallbackAuthorizationPolicy = authorizationOptions.FallbackPolicy is not null,
            FallbackRequirements = authorizationOptions.FallbackPolicy?.Requirements
                .Select(requirement => requirement.GetType().Name)
                .Order()
                .ToArray(),
            ServiceProviderValidation = new
            {
                Development = HearthCalendar.Server.Program.ShouldValidateServiceProvider("Development"),
                Test = HearthCalendar.Server.Program.ShouldValidateServiceProvider("Test"),
                Production = HearthCalendar.Server.Program.ShouldValidateServiceProvider("Production")
            },
            Kestrel = new
            {
                AddsServerHeader = AddsServerHeader()
            }
        });
    }

    [Fact]
    public async Task Cors_denies_unconfigured_browser_origin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/session");
        request.Headers.Add("Origin", "https://calendar.example.home");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            HasAllowOrigin = response.Headers.Contains("Access-Control-Allow-Origin"),
            HasAllowCredentials = response.Headers.Contains("Access-Control-Allow-Credentials")
        });
    }

    [Fact]
    public async Task Cors_allows_configured_browser_origin_with_credentials()
    {
        await using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Security:Cors:AllowedOrigins:0"] = "https://calendar.example.home"
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/admin/session");
        request.Headers.Add("Origin", "https://calendar.example.home");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        await Verifier.Verify(new
        {
            Response = EndpointSnapshot.ForResponse(response),
            AllowOrigin = response.Headers.GetValues("Access-Control-Allow-Origin").Single(),
            AllowCredentials = response.Headers.GetValues("Access-Control-Allow-Credentials").Single()
        });
    }

    [Fact]
    public async Task Calendar_updates_hub_requires_admin_authorization()
    {
        await using var factory = CreateFactory();
        _ = factory.CreateClient();

        var endpoint = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                "/hubs/calendar-updates",
                StringComparison.Ordinal));
        var authorizeData = endpoint.Metadata.OfType<IAuthorizeData>().Single();

        await Verifier.Verify(new
        {
            endpoint.RoutePattern.RawText,
            authorizeData.Policy
        });
    }

    private static bool AddsServerHeader()
    {
        var options = new KestrelServerOptions();

        HearthCalendar.Server.Program.ConfigureKestrelServerOptions(options);

        return options.AddServerHeader;
    }
}
