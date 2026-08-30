using ThisIsMyPC.Core.Packages;

namespace ThisIsMyPC.Modules.Software.Models;

/// <summary>One installable app from the bundled catalog (data ported from CTT winutil, MIT).</summary>
public sealed record SoftwareCatalogEntry(
    string Id,
    string Name,
    string Description,
    string Category,
    string WingetId,
    WingetSource Source,
    string Link,
    bool IsOpenSource);
