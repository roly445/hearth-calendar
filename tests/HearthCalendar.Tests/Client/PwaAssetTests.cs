namespace HearthCalendar.Tests.Client;

public sealed class PwaAssetTests
{
    [Theory]
    [InlineData("src/HearthCalendar.Client/wwwroot/manifest.webmanifest")]
    [InlineData("src/HearthCalendar.Client/wwwroot/service-worker.js")]
    [InlineData("src/HearthCalendar.Client/wwwroot/service-worker.published.js")]
    public void PwaAssetsExist(string relativePath)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath);

        Assert.True(File.Exists(path), $"Expected PWA asset to exist: {relativePath}");
    }

    [Fact]
    public async Task ManifestUsesPublicAppName()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src/HearthCalendar.Client/wwwroot/manifest.webmanifest");

        var manifest = await File.ReadAllTextAsync(path);

        Assert.Contains("\"name\": \"Hearth Calendar\"", manifest);
        Assert.Contains("\"short_name\": \"Hearth\"", manifest);
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
}
