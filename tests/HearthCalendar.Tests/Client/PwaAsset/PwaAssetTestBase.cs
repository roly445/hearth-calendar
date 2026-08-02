using System.Text.Json;
using System.Text.Json.Serialization;

namespace HearthCalendar.Tests.Client;

public abstract class PwaAssetTestBase
{
    protected static string FindRepositoryRoot()
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

    protected static object DescribeAsset(string root, string path)
    {
        var info = new FileInfo(path);

        return new
        {
            Path = Path.GetRelativePath(root, path).Replace('\\', '/'),
            info.Exists
        };
    }

    protected sealed record WebManifestSnapshot(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("short_name")] string ShortName,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("start_url")] string StartUrl,
        [property: JsonPropertyName("display")] string Display,
        [property: JsonPropertyName("background_color")] string BackgroundColor,
        [property: JsonPropertyName("theme_color")] string ThemeColor,
        [property: JsonPropertyName("prefer_related_applications")] bool PreferRelatedApplications,
        [property: JsonPropertyName("icons")] WebManifestIconSnapshot[] Icons);

    protected sealed record WebManifestIconSnapshot(
        [property: JsonPropertyName("src")] string Src,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("sizes")] string Sizes);
}
