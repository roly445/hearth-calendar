namespace HearthCalendar.Server;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();

        app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")));
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}

public sealed record HealthResponse(string Status);
