using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HearthCalendar.Tests.Server;

public abstract class HealthEndpointTestBase
{
    protected static WebApplicationFactory<HearthCalendar.Server.Program> CreateFactory(
        IReadOnlyDictionary<string, string?>? configurationValues = null) =>
        new WebApplicationFactory<HearthCalendar.Server.Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = "Host=localhost;Database=hearth_calendar_test",
                        ["Database:SchemaName"] = "hearth_calendar_test"
                    };

                    foreach (var pair in configurationValues ?? new Dictionary<string, string?>())
                    {
                        values[pair.Key] = pair.Value;
                    }

                    configuration.AddInMemoryCollection(values);
                });
            });
}
