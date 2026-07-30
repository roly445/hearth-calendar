using System.Text.Json;
using System.Text.Json.Serialization;

namespace HearthCalendar.Tests.Client;

public sealed class PwaAssetTests
{
    [Fact]
    public async Task PwaAssetsMatchSnapshot()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "src/HearthCalendar.Client/wwwroot/manifest.webmanifest");
        var serviceWorkerPath = Path.Combine(root, "src/HearthCalendar.Client/wwwroot/service-worker.js");
        var publishedServiceWorkerPath = Path.Combine(root, "src/HearthCalendar.Client/wwwroot/service-worker.published.js");
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        var publishedServiceWorker = await File.ReadAllTextAsync(publishedServiceWorkerPath);
        var offlineStorage = await File.ReadAllTextAsync(Path.Combine(root, "src/HearthCalendar.Client/wwwroot/offline-calendar.js"));
        var manifest = JsonSerializer.Deserialize<WebManifestSnapshot>(manifestJson)
            ?? throw new InvalidOperationException("Could not parse web manifest.");

        await Verifier.Verify(new
        {
            Manifest = manifest,
            Installability = new
            {
                HasName = !string.IsNullOrWhiteSpace(manifest.Name),
                HasShortName = !string.IsNullOrWhiteSpace(manifest.ShortName),
                HasStandaloneDisplay = manifest.Display == "standalone",
                HasStartUrl = !string.IsNullOrWhiteSpace(manifest.StartUrl),
                HasMaskableSizedIcons = manifest.Icons.Select(icon => icon.Sizes).Order().ToArray()
            },
            PublishedServiceWorkerPolicy = new
            {
                UsesJsonAllowlist = publishedServiceWorker.Contains("offlineJsonAssetsInclude", StringComparison.Ordinal),
                AvoidsBroadJsonCaching = !publishedServiceWorker.Contains("/\\.json$/", StringComparison.Ordinal),
                ExcludesCommands = publishedServiceWorker.Contains("/^\\/commands\\//", StringComparison.Ordinal),
                ExcludesQueries = publishedServiceWorker.Contains("/^\\/queries\\//", StringComparison.Ordinal),
                ExcludesHubs = publishedServiceWorker.Contains("/^\\/hubs\\//", StringComparison.Ordinal),
                ExcludesAuth = publishedServiceWorker.Contains("/^\\/auth\\//", StringComparison.Ordinal),
                ExcludesFeeds = publishedServiceWorker.Contains("/^\\/feeds\\//", StringComparison.Ordinal),
                ExcludesApi = publishedServiceWorker.Contains("/^\\/api\\//", StringComparison.Ordinal)
            },
            OfflineStoragePolicy = new
            {
                UsesIndexedDb = offlineStorage.Contains("indexedDB.open", StringComparison.Ordinal),
                AvoidsLocalStorage = !offlineStorage.Contains("localStorage", StringComparison.Ordinal)
            },
            Assets = new[]
            {
                DescribeAsset(root, manifestPath),
                DescribeAsset(root, serviceWorkerPath),
                DescribeAsset(root, publishedServiceWorkerPath),
                DescribeAsset(root, Path.Combine(root, "src/HearthCalendar.Client/wwwroot/offline-calendar.js"))
            },
            IconCount = manifest.Icons.Length
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HearthCalendar.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static object DescribeAsset(string root, string path)
    {
        var info = new FileInfo(path);

        return new
        {
            Path = Path.GetRelativePath(root, path).Replace('\\', '/'),
            info.Exists
        };
    }

    private sealed record WebManifestSnapshot(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("short_name")] string ShortName,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("start_url")] string StartUrl,
        [property: JsonPropertyName("display")] string Display,
        [property: JsonPropertyName("background_color")] string BackgroundColor,
        [property: JsonPropertyName("theme_color")] string ThemeColor,
        [property: JsonPropertyName("prefer_related_applications")] bool PreferRelatedApplications,
        [property: JsonPropertyName("icons")] WebManifestIconSnapshot[] Icons);

    private sealed record WebManifestIconSnapshot(
        [property: JsonPropertyName("src")] string Src,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("sizes")] string Sizes);
}
