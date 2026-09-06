using Microsoft.Win32;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.Services;

internal static class InstallerPreferenceImport
{
    private const string KeyPath = @"Software\No More Secrets\ThisIsMyPC\InstallOptions";

    internal static void Apply(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key is null)
            return;

        Import(key, settings, AppSettingKeys.AutoStart);
        Import(key, settings, AppSettingKeys.TrayMode);
        Import(key, settings, AppSettingKeys.UpdateCheck);
    }

    private static void Import(RegistryKey key, ISettingsService settings, string name)
    {
        if (key.GetValue(name) is string value && value is "0" or "1")
            settings.SetApp(name, value);
        key.DeleteValue(name, throwOnMissingValue: false);
    }
}
