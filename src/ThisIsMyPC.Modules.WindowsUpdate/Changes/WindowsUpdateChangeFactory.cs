using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.WindowsUpdate.Models;

namespace ThisIsMyPC.Modules.WindowsUpdate.Changes;

public static class WindowsUpdateChangeFactory
{
    public const string ModuleId = "Windows Update";

    /// <summary>
    /// WU orchestrator policies: the GPCache overrides the policy hive, so the cache is
    /// cleared around EVERY mutation — including restores to Not configured, where a
    /// stale cache would keep the removed policy alive (unlike the Annoyances
    /// suppress-only rule, which covers informational vectors).
    /// </summary>
    internal static readonly SettingEnforcement WUPolicyEnforcement = new()
    {
        GPCacheEntries = [WindowsUpdateRegistryPaths.GPCacheKeyPath],
        ReversionVectors = ["Group Policy refresh", "Windows feature updates"],
    };

    /// <summary>
    /// Creates a policy toggle change. <paramref name="configure"/> true writes
    /// <see cref="UpdatePolicySetting.ConfiguredValue"/>; false deletes the value
    /// (empty AfterValue = value absent, the PowerModule convention).
    /// <paramref name="gpCache"/> attaches the GPCache enforcement — true for
    /// orchestrator-read policies, false for Delivery Optimization (DoSvc reads its
    /// policy key directly).
    /// </summary>
    public static ChangeDescriptor CreateToggle(UpdatePolicySetting setting, bool configure, bool gpCache = true)
    {
        return new ChangeDescriptor
        {
            ModuleId = ModuleId,
            SettingId = setting.Id,
            DisplayName = setting.DisplayName,
            SystemLocation = $@"{setting.RegistryKeyPath}\{setting.RegistryValueName}",
            BeforeValue = setting.CurrentValue,
            AfterValue = configure ? setting.ConfiguredValue : string.Empty,
            BeforeDisplay = setting.IsConfigured ? "Configured" : "Not configured",
            AfterDisplay = configure ? "Configured" : "Not configured",
            ValueType = setting.ValueType,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
            Enforcement = gpCache ? WUPolicyEnforcement : null,
        };
    }

    /// <summary>
    /// One atomic ChangeGroup pinning (or unpinning) the Windows feature release: the
    /// three WindowsUpdate policy values together. Null when the pin is unavailable
    /// (empty group = DisplayVersion unreadable at scan time).
    /// </summary>
    public static ChangeGroup? CreateVersionPinGroup(IReadOnlyList<UpdatePolicySetting> versionPin, bool configure)
    {
        if (versionPin.Count == 0)
            return null;

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = "Stay on the current Windows version",
            Description = versionPin[0].Description,
            Changes = versionPin.Select(setting => CreateToggle(setting, configure)).ToList(),
        };
    }
}
