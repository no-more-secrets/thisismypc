using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>Curated static search inventory for the Startup &amp; Services tab (5-3).</summary>
public sealed class StartupSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => "Startup & Services";

    public IReadOnlyList<SearchEntry> GetSearchEntries() =>
    [
        new(ModuleId, "startup-entries", "Startup programs",
            "Enable or disable programs that launch at sign-in (Run keys, startup folders).",
            ["Run key", "StartupApproved", "autostart", "boot"]),
        new(ModuleId, "services", "Windows services",
            "View and change service start types; start, stop, and restart services.",
            ["SCM", "service", "start type", "disabled", "automatic"]),
        new(ModuleId, "scheduled-tasks", "Scheduled tasks",
            "Audit and disable scheduled tasks, classified by what they do.",
            ["Task Scheduler", "schtasks", "telemetry tasks"]),
    ];
}
