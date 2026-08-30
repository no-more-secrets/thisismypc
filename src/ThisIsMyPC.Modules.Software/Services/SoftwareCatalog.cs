using System.Reflection;
using System.Text.Json;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Modules.Software.Models;

namespace ThisIsMyPC.Modules.Software.Services;

/// <summary>
/// Loads the bundled app catalog from the embedded <c>Data/catalog.json</c>.
/// Data is ported from CTT winutil's applications.json (MIT License,
/// Copyright (c) Chris Titus Tech); parsed with JsonDocument (NativeAOT-safe).
/// </summary>
public static class SoftwareCatalog
{
    private const string ResourceName = "ThisIsMyPC.Modules.Software.Data.catalog.json";

    private static readonly Lazy<IReadOnlyList<SoftwareCatalogEntry>> _entries = new(LoadFromResource);

    public static IReadOnlyList<SoftwareCatalogEntry> Entries => _entries.Value;

    private static IReadOnlyList<SoftwareCatalogEntry> LoadFromResource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded catalog resource '{ResourceName}' is missing.");
        return Parse(stream);
    }

    /// <summary>Parses a catalog document. Exposed for tests.</summary>
    public static IReadOnlyList<SoftwareCatalogEntry> Parse(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);

        var entries = new List<SoftwareCatalogEntry>();
        foreach (var app in document.RootElement.GetProperty("apps").EnumerateArray())
        {
            entries.Add(new SoftwareCatalogEntry(
                Id: app.GetProperty("id").GetString()!,
                Name: app.GetProperty("name").GetString()!,
                Description: app.GetProperty("description").GetString()!,
                Category: app.GetProperty("category").GetString()!,
                WingetId: app.GetProperty("wingetId").GetString()!,
                Source: app.GetProperty("source").GetString() == "msstore"
                    ? WingetSource.MsStore
                    : WingetSource.Winget,
                Link: app.GetProperty("link").GetString()!,
                IsOpenSource: app.GetProperty("foss").GetBoolean()));
        }

        return entries.AsReadOnly();
    }
}
