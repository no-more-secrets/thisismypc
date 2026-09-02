using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Installer.Services;

/// <summary>
/// On an upgrade the options page starts from what the user already chose in
/// the app, not from the defaults, so running the installer again never
/// silently flips a setting.
/// </summary>
public static class ExistingSettings
{
    public static InstallOptions? Read()
    {
        var path = Path.Combine(AppConstants.DataDirectoryPath, "settings.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var settings = new SettingsService(path);
            settings.Initialize();
            if (settings.LoadError is not null)
                return null;
            return new InstallOptions(
                InstallFolderRules.DefaultFolder,
                DesktopShortcut: true,
                StartWithWindows: settings.GetAppBool(AppSettingKeys.AutoStart, false),
                CheckForUpdates: settings.GetAppBool(AppSettingKeys.UpdateCheck, true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
