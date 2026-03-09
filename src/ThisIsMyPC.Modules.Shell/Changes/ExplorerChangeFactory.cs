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
            BeforeValue = enable ? pref.DisabledValue : pref.EnabledValue,
            AfterValue = enable ? pref.EnabledValue : pref.DisabledValue,
            BeforeDisplay = enable ? "Disabled" : "Enabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = pref.ValueType,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = pref.RestartRequirement,
        };
    }
}
