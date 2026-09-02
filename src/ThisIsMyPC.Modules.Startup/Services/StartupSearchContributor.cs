using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>Curated static search inventory for the Startup &amp; Services page (the Autoruns inventory).</summary>
public sealed class StartupSearchContributor : ISearchSettingsContributor
{
    public string ModuleId => "Startup & Services";

    public IReadOnlyList<SearchEntry> GetSearchEntries() =>
    [
        new(ModuleId, "autoruns", "Autoruns",
            "Everything that starts on its own: sign-in programs, shell extensions, services, drivers, scheduled tasks, and more, disabled the way Sysinternals Autoruns does it.",
            ["Run key", "startup", "autostart", "boot", "service", "driver", "Task Scheduler", "shell extension", "AutorunsDisabled"]),
    ];
}
