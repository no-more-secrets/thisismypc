using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Changes;

public static class ExplorerChangeFactory
{
    private const string ModuleId = "Explorer";

    public static ChangeDescriptor CreateToggle(ExplorerPreference pref, bool enable)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = pref.Id,
            DisplayName = pref.DisplayName,
            SystemLocation = $@"{pref.RegistryKeyPath}\{pref.RegistryValueName}",
            BeforeValue = pref.CurrentValue,
            AfterValue = enable ? pref.EnabledValue : pref.DisabledValue,
            BeforeDisplay = pref.IsEnabled ? "Enabled" : "Disabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = pref.ValueType,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = pref.RestartRequirement,
        };
    }
}
