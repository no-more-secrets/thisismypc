using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Modules.Software.Models;

namespace ThisIsMyPC.Modules.Software.Actions;

/// <summary>
/// Builds pending-queue actions for catalog apps. ActionId format is
/// "install:{catalogId}" / "uninstall:{catalogId}"; <see cref="SoftwareModule"/>
/// resolves the catalog id back to a winget id and source at execution time.
/// </summary>
public static class SoftwareActionFactory
{
    public const string InstallPrefix = "install:";
    public const string UninstallPrefix = "uninstall:";
    public const string UpgradePrefix = "upgrade:";
    public const string AppxRemovePrefix = "appx-remove:";
    public const string AppxReinstallPrefix = "appx-reinstall:";

    public static ActionDescriptor CreateInstall(SoftwareCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ActionDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            ActionId = InstallPrefix + entry.Id,
            DisplayName = $"Install {entry.Name}",
            Detail = $"winget: {entry.WingetId}",
            UndoHint = "Uninstall from the app catalog or Windows Settings.",
        };
    }

    public static ActionDescriptor CreateUninstall(SoftwareCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ActionDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            ActionId = UninstallPrefix + entry.Id,
            DisplayName = $"Uninstall {entry.Name}",
            Detail = $"winget: {entry.WingetId} (runs the app's own uninstaller)",
            UndoHint = "Reinstall from the app catalog.",
        };
    }

    /// <summary>Update action ids embed the winget package id directly; updates are not limited to catalog apps.</summary>
    public static ActionDescriptor CreateUpgrade(Core.Packages.UpgradableWingetPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new ActionDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            ActionId = UpgradePrefix + package.PackageId,
            DisplayName = $"Update {package.Name}",
            Detail = $"winget: {package.PackageId} {package.InstalledVersion} to {package.AvailableVersion}",
            UndoHint = null,
        };
    }

    public static ActionDescriptor CreateAppxRemove(WindowsAppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ActionDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            ActionId = AppxRemovePrefix + entry.Id,
            DisplayName = $"Remove {entry.Name}",
            Detail = $"appx: {entry.PackageId} (all users, stops auto-install for new profiles)",
            UndoHint = entry.CanReinstall
                ? "Reinstall from this page or the Microsoft Store."
                : "No Store listing. Reinstall requires a Windows feature update or DISM.",
        };
    }

    public static ActionDescriptor CreateAppxReinstall(WindowsAppEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ActionDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            ActionId = AppxReinstallPrefix + entry.Id,
            DisplayName = $"Reinstall {entry.Name}",
            Detail = $"msstore: {entry.StoreId}",
            UndoHint = "Remove again from this page.",
        };
    }
}
