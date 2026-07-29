using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HearthCalendar.Tests.Server;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task HealthEndpointReturnsHealthy()
    {
        await using var factory = new WebApplicationFactory<HearthCalendar.Server.Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HealthEndpointIsExplicitlyAnonymous()
    {
        await using var factory = new WebApplicationFactory<HearthCalendar.Server.Program>();
        _ = factory.CreateClient();

        var endpoint = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Single(endpoint => endpoint.DisplayName?.Contains("HTTP: GET /health", StringComparison.Ordinal) == true);

        Assert.Contains(endpoint.Metadata, metadata => metadata is IAllowAnonymous);
    }

    [Fact]
    public async Task AppUsesFallbackAuthorization()
    {
        await using var factory = new WebApplicationFactory<HearthCalendar.Server.Program>();
        _ = factory.CreateClient();

        var authorizationOptions = factory.Services
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value;

        Assert.NotNull(authorizationOptions.FallbackPolicy);
        Assert.Contains(
            authorizationOptions.FallbackPolicy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void ServiceProviderValidationRunsInDevelopmentAndTest()
    {
        Assert.True(HearthCalendar.Server.Program.ShouldValidateServiceProvider("Development"));
        Assert.True(HearthCalendar.Server.Program.ShouldValidateServiceProvider("Test"));
        Assert.False(HearthCalendar.Server.Program.ShouldValidateServiceProvider("Production"));
    }

    [Fact]
    public async Task HealthEndpointIncludesSecurityHeaders()
    {
        await using var factory = new WebApplicationFactory<HearthCalendar.Server.Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains(
            "default-src 'self'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
