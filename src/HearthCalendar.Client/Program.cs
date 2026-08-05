using BluQube.Attributes;
using BluQube.Commands;
using BluQube.Queries;
using HearthCalendar.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace HearthCalendar.Client;

[BluQubeRequester]
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddTransient<CookieCredentialHandler>();
        builder.Services
            .AddHttpClient(
                "hearth-calendar",
                client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
            .AddHttpMessageHandler<CookieCredentialHandler>();
        builder.Services.AddHttpClient(
                "bluqube",
                client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
            .AddHttpMessageHandler<CookieCredentialHandler>();
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("hearth-calendar"));
        builder.Services.AddScoped<ICommandRunner, CommandRunner>();
        builder.Services.AddScoped<IQueryRunner, QueryRunner>();
        builder.Services.AddTransient<CommandResultConverter>();
        builder.Services.AddBluQubeRequesters();
        builder.Services.AddScoped<CalendarUpdateClient>();
        builder.Services.AddScoped<OfflineCalendarStore>();
        builder.Services.AddScoped<AdminSessionClient>();

        await builder.Build().RunAsync();
    }
}
