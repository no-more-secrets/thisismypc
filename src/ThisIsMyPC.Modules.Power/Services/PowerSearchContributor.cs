using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.Modules.Power.Services;

/// <summary>Curated static search inventory for the Power Plans tab (5-3).</summary>
public sealed class PowerSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => "Power Plans";

    public IReadOnlyList<SearchEntry> GetSearchEntries() =>
    [
        new(ModuleId, "active-plan", "Active power plan",
            "Switch between Balanced, High performance, and other plans.",
            ["powercfg", "power plan", "high performance", "balanced"]),
        new(ModuleId, "plan-settings", "Power plan settings",
            "Per-plan AC/DC settings: sleep, display timeout, processor limits.",
            ["sleep", "timeout", "processor", "AC", "DC", "brightness"]),
        new(ModuleId, "modern-standby", "Modern Standby (S0) toggle",
            "Switch between Modern Standby and legacy S3 sleep where firmware allows.",
            ["PlatformAoAcOverride", "S3", "S0", "sleep states"]),
    ];
}
