using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ThisIsMyPC.App.Services;

/// <summary>Bundled catalog icons, shared for the lifetime of the UI. No network requests.</summary>
public static class SoftwareIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Bitmap? Get(string id)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(id, out var cached)) return cached;
            var uri = new Uri($"avares://ThisIsMyPC.App/Assets/Software/{Uri.EscapeDataString(id)}.png");
            Bitmap? icon = null;
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                icon = new Bitmap(stream);
            }
            Cache.Add(id, icon);
            return icon;
        }
    }
}
