namespace ThisIsMyPC.Modules.WindowsUpdate;

public static class WindowsUpdateRegistryPaths
{
    public const string WindowsUpdatePoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    public const string AuPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    public const string DeliveryOptimizationPoliciesKeyPath =
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    /// <summary>
    /// The Update Orchestrator's policy cache. Stale entries here override the policy
    /// hive, so every WU policy mutation carries this as a GPCache enforcement entry
    /// (cleared before apply and after revert; Windows rebuilds it).
    /// </summary>
    public const string GPCacheKeyPath =
        @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache";

    /// <summary>Live OS version info; DisplayVersion (e.g. "24H2") feeds the version pin.</summary>
    public const string CurrentVersionKeyPath =
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <summary>
    /// The Windows Update Settings-page state store (not a policy hive). Values here
    /// are what the Settings toggles themselves write — no GPCache, no SKU gating.
    /// </summary>
    public const string UxSettingsKeyPath =
        @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    public static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
    {
        var lastSeparator = systemLocation.LastIndexOf('\\');
        return (systemLocation[..lastSeparator], systemLocation[(lastSeparator + 1)..]);
    }
}
