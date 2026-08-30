namespace ThisIsMyPC.Modules.Software.Models;

/// <summary>
/// One removable inbox app (data ported from CTT winutil, MIT).
/// <see cref="PackageId"/> is the AppX package name (family name minus publisher
/// suffix); <see cref="StoreId"/> enables reinstall through the msstore winget
/// source and is empty when no Store listing exists.
/// </summary>
public sealed record WindowsAppEntry(
    string Id,
    string Name,
    string Description,
    string PackageId,
    string StoreId,
    string Category)
{
    public bool CanReinstall => StoreId.Length > 0;
}
