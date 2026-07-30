using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HearthCalendar.Tests.Server;

public sealed class HealthEndpointTests
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
            }
        });
    }

    private static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory() =>
        new WebApplicationFactory<HearthCalendar.Server.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = "Host=localhost;Database=hearth_calendar_test",
                        ["Database:SchemaName"] = "hearth_calendar_test"
                    });
                });
            });
}
