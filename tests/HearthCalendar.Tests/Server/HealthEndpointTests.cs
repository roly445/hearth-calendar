using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

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
}
