using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.Modules.Annoyances.Changes;

public static class AnnoyanceChangeFactory
{
    public const string ModuleId = "Windows Annoyances";

    /// <summary>
    /// Creates a toggle change. <paramref name="suppress"/> true writes the suppressing
    /// value; false restores the Windows default. BeforeValue is the preference's live
    /// CurrentValue (a missing registry value scans as the default), preserving true
    /// before-state fidelity for revert. SettingEnforcement stays null per FR139.
    /// </summary>
    public static ChangeDescriptor CreateToggle(AnnoyancePreference pref, bool suppress)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = pref.Id,
            DisplayName = pref.DisplayName,
            SystemLocation = $@"{pref.RegistryKeyPath}\{pref.RegistryValueName}",
            BeforeValue = pref.CurrentValue,
            AfterValue = suppress ? pref.SuppressedValue : pref.DefaultValue,
            BeforeDisplay = pref.IsSuppressed ? "Suppressed" : "Windows default",
            AfterDisplay = suppress ? "Suppressed" : "Windows default",
            ValueType = pref.ValueType,
            Category = suppress ? ChangeCategory.Disable : ChangeCategory.Enable,
            RestartRequirement = pref.RestartRequirement,
        };
    }
}
