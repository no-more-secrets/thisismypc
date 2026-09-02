using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.Modules.Power.Changes;

/// <summary>Builds ChangeDescriptors that switch the active power plan via powrprof.dll.</summary>
public static class PowerPlanChangeFactory
{
    public const string ModuleId = "Power Plans";

    /// <summary>The active plan is one logical setting; re-selection re-stages this id.</summary>
    public const string ActivePlanSettingId = "active-power-plan";

    /// <summary>
    /// Group Policy "Specify a custom active power plan": while this value
    /// names a scheme, Windows refuses every other switch (Win32 error 1260).
    /// winutil writes it when it activates its Ultimate plan.
    /// </summary>
    public const string ActivePlanPolicyKeyPath = @"HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings";
    public const string ActivePlanPolicyValueName = "ActivePowerScheme";

    /// <summary>SettingId prefix for individual plan settings; suffix is :AC or :DC.</summary>
    public const string SettingIdPrefix = "power-setting:";

    public const string ModernStandbySettingId = "modern-standby";
    public const string ModernStandbyKeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";
    public const string ModernStandbyValueName = "PlatformAoAcOverride";

    public const string HibernateSettingId = "hibernation";
    public const string HibernateValueName = "HibernateEnabled";

    public const string UltimatePerformanceSettingId = "ultimate-performance";

    /// <summary>The hidden scheme Windows ships; installing means duplicating it.</summary>
    public static readonly Guid UltimatePerformanceSourceGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>Description written on our duplicate so scan and removal find it across locales.</summary>
    public const string UltimatePerformanceMarker = "Ultimate Performance plan installed by ThisIsMyPC";

    /// <summary>SettingId prefix for a plan the person creates; the suffix is the plan name.</summary>
    public const string CreatePlanPrefix = "create-plan:";

    /// <summary>Description written on a created plan so undo deletes only what this app made.</summary>
    public const string CreatedPlanMarker = "Created by ThisIsMyPC";

    /// <summary>
    /// Creates a plan as a copy of <paramref name="source"/> (every setting
    /// carried over) under a new name. Reversible: undo deletes the copy,
    /// found by name and marker, and refuses while it is the active plan.
    /// </summary>
    public static ChangeDescriptor CreatePlanChange(string name, PowerPlan source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        var trimmed = name.Trim();
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = CreatePlanPrefix + trimmed,
            DisplayName = $"Create power plan {trimmed}",
            SystemLocation = $"powrprof:PowerDuplicateScheme {source.PlanGuid:D}",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "Not present",
            AfterDisplay = $"Copy of {source.Name}",
            ValueType = ChangeValueType.PowerPlan_Setting,
            Category = ChangeCategory.Create,
            RestartRequirement = RestartRequirement.None,
        };
    }

    /// <summary>SettingId prefix for a deleted stock plan put back; the suffix is its GUID.</summary>
    public const string AddStockPlanPrefix = "add-stock-plan:";

    /// <summary>
    /// Puts a deleted stock plan back under its own GUID with Windows'
    /// default settings. Reversible: undo deletes the plan again, never
    /// while it is active.
    /// </summary>
    public static ChangeDescriptor CreateStockPlanRestore(StockPowerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = AddStockPlanPrefix + plan.PlanGuid.ToString("D"),
            DisplayName = $"Add power plan {plan.Name}",
            SystemLocation = $"powrprof:PowerDuplicateScheme {plan.PlanGuid:D}",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "Not present",
            AfterDisplay = "Windows default",
            ValueType = ChangeValueType.PowerPlan_Setting,
            Category = ChangeCategory.Create,
            RestartRequirement = RestartRequirement.None,
        };
    }

    /// <summary>The plan a create change copies: the GUID at the end of its SystemLocation.</summary>
    public static bool TryParseSourceGuid(string systemLocation, out Guid sourceGuid)
    {
        ArgumentNullException.ThrowIfNull(systemLocation);
        var lastSpace = systemLocation.LastIndexOf(' ');
        return Guid.TryParse(lastSpace < 0 ? systemLocation : systemLocation[(lastSpace + 1)..], out sourceGuid);
    }

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
            DisplayName = $"{plan.Name}: {setting.Name} ({scopeDisplay})",
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

    /// <summary>
    /// Toggles the hiberfile via CallNtPowerInformation, like powercfg /hibernate.
    /// Disabling also removes Fast Startup and the Hibernate power-menu entry.
    /// </summary>
    public static ChangeDescriptor CreateHibernateToggle(bool currentlyEnabled, bool enable)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = HibernateSettingId,
            DisplayName = "Hibernation",
            SystemLocation = "powrprof:SystemReserveHiberFile",
            BeforeValue = currentlyEnabled ? "1" : "0",
            AfterValue = enable ? "1" : "0",
            BeforeDisplay = currentlyEnabled ? "Enabled" : "Disabled",
            AfterDisplay = enable ? "Enabled" : "Disabled",
            ValueType = ChangeValueType.PowerPlan_Setting,
            Category = enable ? ChangeCategory.Enable : ChangeCategory.Disable,
            RestartRequirement = RestartRequirement.None,
        };
    }

    /// <summary>
    /// Installs (duplicates the hidden scheme, marks the copy) or removes the
    /// Ultimate Performance plan. Removal targets the marked or matching plan
    /// and fails while that plan is active.
    /// </summary>
    public static ChangeDescriptor CreateUltimatePerformanceToggle(bool currentlyInstalled, bool install)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = UltimatePerformanceSettingId,
            DisplayName = "Ultimate Performance plan",
            SystemLocation = $"powrprof:PowerDuplicateScheme {UltimatePerformanceSourceGuid:D}",
            BeforeValue = currentlyInstalled ? "1" : "0",
            AfterValue = install ? "1" : "0",
            BeforeDisplay = currentlyInstalled ? "Installed" : "Not installed",
            AfterDisplay = install ? "Installed" : "Not installed",
            ValueType = ChangeValueType.PowerPlan_Setting,
            Category = install ? ChangeCategory.Create : ChangeCategory.Delete,
            RestartRequirement = RestartRequirement.None,
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
