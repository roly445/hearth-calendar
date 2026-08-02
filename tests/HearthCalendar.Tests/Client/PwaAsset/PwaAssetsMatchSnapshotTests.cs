using System.Text.Json;
using System.Text.Json.Serialization;

namespace HearthCalendar.Tests.Client;

public sealed class PwaAssetsMatchSnapshotTests : PwaAssetTestBase
{
    [Fact]
    public async Task PwaAssetsMatchSnapshot()
    {
        var root = FindRepositoryRoot();
        var manifestSourcePath = Path.Combine(root, "src/HearthCalendar.Client/Assets/Pwa/manifest.webmanifest");
        var serviceWorkerSourcePath = Path.Combine(root, "src/HearthCalendar.Client/Assets/Scripts/service-worker.ts");
        var publishedServiceWorkerSourcePath = Path.Combine(root, "src/HearthCalendar.Client/Assets/Scripts/service-worker.published.ts");
        var offlineStorageSourcePath = Path.Combine(root, "src/HearthCalendar.Client/Assets/Scripts/offline-calendar.ts");
        var manifestPath = Path.Combine(root, "src/HearthCalendar.Client/wwwroot/manifest.webmanifest");
        var serviceWorkerPath = Path.Combine(root, "src/HearthCalendar.Client/wwwroot/service-worker.js");
        var publishedServiceWorkerPath = Path.Combine(root, "src/HearthCalendar.Client/wwwroot/service-worker.published.js");
        var offlineStoragePath = Path.Combine(root, "src/HearthCalendar.Client/wwwroot/offline-calendar.js");
        var manifestJson = await File.ReadAllTextAsync(manifestSourcePath);
        var publishedServiceWorker = await File.ReadAllTextAsync(publishedServiceWorkerSourcePath);
        var offlineStorage = await File.ReadAllTextAsync(offlineStorageSourcePath);
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
                UsesJsonAllowlist = publishedServiceWorker.Contains("JsonAssetsInclude", StringComparison.Ordinal),
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
                DescribeAsset(root, manifestSourcePath),
                DescribeAsset(root, serviceWorkerSourcePath),
                DescribeAsset(root, publishedServiceWorkerSourcePath),
                DescribeAsset(root, offlineStorageSourcePath),
                DescribeAsset(root, manifestPath),
                DescribeAsset(root, serviceWorkerPath),
                DescribeAsset(root, publishedServiceWorkerPath),
                DescribeAsset(root, offlineStoragePath)
            },
            IconCount = manifest.Icons.Length
        });
    }
}
