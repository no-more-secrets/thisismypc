using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.Modules.Power.Changes;

/// <summary>Builds ChangeDescriptors that switch the active power plan via powrprof.dll.</summary>
public static class PowerPlanChangeFactory
{
    public const string ModuleId = "Power Plans";

    /// <summary>The active plan is one logical setting — re-selection re-stages this id.</summary>
    public const string ActivePlanSettingId = "active-power-plan";

    /// <summary>SettingId prefix for individual plan settings; suffix is :AC or :DC.</summary>
    public const string SettingIdPrefix = "power-setting:";

    public const string ModernStandbySettingId = "modern-standby";
    public const string ModernStandbyKeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";
    public const string ModernStandbyValueName = "PlatformAoAcOverride";

    /// <summary>Stages one AC or DC value-index write for an individual plan setting.</summary>
    public static ChangeDescriptor CreateSettingChange(
        PowerPlan plan, PowerSetting setting, bool ac, uint currentIndex, uint newIndex)
    {
        var scope = ac ? "AC" : "DC";
        var scopeDisplay = ac ? "Plugged in" : "On battery";
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = $"{SettingIdPrefix}{plan.PlanGuid:D}:{setting.SettingGuid:D}:{scope}",
            DisplayName = $"{plan.Name} — {setting.Name} ({scopeDisplay})",
            SystemLocation = $"{plan.PlanGuid:D}/{setting.SubgroupGuid:D}/{setting.SettingGuid:D}/{scope}",
            BeforeValue = currentIndex.ToString(),
            AfterValue = newIndex.ToString(),
            BeforeDisplay = setting.FormatIndex(currentIndex),
            AfterDisplay = setting.FormatIndex(newIndex),
            ValueType = ChangeValueType.PowerPlan_Setting,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
        };
    }

    /// <summary>
    /// Toggles Modern Standby via PlatformAoAcOverride (never the removed CsEnabled).
    /// 0 disables Modern Standby at next boot; restoring deletes the value (empty
    /// After/BeforeValue encodes "value absent", mirroring the StartupApproved blob
    /// convention).
    /// </summary>
    public static ChangeDescriptor CreateModernStandbyToggle(int? currentValue, bool disable)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = ModernStandbySettingId,
            DisplayName = "Modern Standby (S0 low-power idle)",
            SystemLocation = $@"{ModernStandbyKeyPath}\{ModernStandbyValueName}",
            BeforeValue = currentValue?.ToString() ?? string.Empty,
            AfterValue = disable ? "0" : string.Empty,
            BeforeDisplay = currentValue == 0 ? "Disabled (S3 sleep)" : "Windows default (Modern Standby)",
            AfterDisplay = disable ? "Disabled (S3 sleep)" : "Windows default (Modern Standby)",
            ValueType = ChangeValueType.Registry_DWord,
            Category = disable ? ChangeCategory.Disable : ChangeCategory.Enable,
            RestartRequirement = RestartRequirement.Reboot,
        };
    }

    public static ChangeDescriptor CreateActivePlanChange(PowerPlan currentActive, PowerPlan newPlan)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = ActivePlanSettingId,
            DisplayName = $"Active power plan: {newPlan.Name}",
            SystemLocation = "powrprof:ActiveScheme",
            BeforeValue = currentActive.PlanGuid.ToString("D"),
            AfterValue = newPlan.PlanGuid.ToString("D"),
            BeforeDisplay = currentActive.Name,
            AfterDisplay = newPlan.Name,
            ValueType = ChangeValueType.PowerPlan_Setting,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
        };
    }
}
