using System.Reflection;
using System.Text.Json;
using ThisIsMyPC.Modules.Software.Models;

namespace ThisIsMyPC.Modules.Software.Services;

/// <summary>
/// Loads the bundled removable inbox-app list from the embedded
/// <c>Data/windows-apps.json</c> (ported from CTT winutil's appx.json, MIT).
/// </summary>
public static class WindowsAppsCatalog
{
    private const string ResourceName = "ThisIsMyPC.Modules.Software.Data.windows-apps.json";

    private static readonly Lazy<IReadOnlyList<WindowsAppEntry>> _entries = new(LoadFromResource);

    public static IReadOnlyList<WindowsAppEntry> Entries => _entries.Value;

    private static IReadOnlyList<WindowsAppEntry> LoadFromResource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        return Parse(stream);
    }

    /// <summary>Parses a windows-apps document. Exposed for tests.</summary>
    public static IReadOnlyList<WindowsAppEntry> Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);

        var entries = new List<WindowsAppEntry>();
        foreach (var app in document.RootElement.GetProperty("apps").EnumerateArray())
        {
            entries.Add(new WindowsAppEntry(
                Id: app.GetProperty("id").GetString()!,
                Name: app.GetProperty("name").GetString()!,
                Description: app.GetProperty("description").GetString()!,
                PackageId: app.GetProperty("packageId").GetString()!,
                StoreId: app.GetProperty("storeId").GetString()!,
                Category: app.GetProperty("category").GetString()!));
        }

        return entries.AsReadOnly();
    }
}
