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
}
