using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Enumerates all scheduled tasks, applies classification heuristics and any
/// persisted user overrides, and flags enforcement companion tasks.
/// </summary>
public sealed class ScheduledTaskScanner
{
    private readonly IScheduledTaskService _taskService;
    private readonly TaskClassificationOverrideStore _overrides;

    public ScheduledTaskScanner(IScheduledTaskService taskService, TaskClassificationOverrideStore overrides)
    {
        _taskService = taskService;
        _overrides = overrides;
    }

    /// <summary>Non-null after Scan() when task enumeration itself failed (list is then empty).</summary>
    public string? LastScanError { get; private set; }

    public IReadOnlyList<ScheduledTaskEntry> Scan()
    {
        LastScanError = null;
        var enumerated = _taskService.EnumerateAll();
        if (!enumerated.IsSuccess || enumerated.Value is null)
        {
            LastScanError = enumerated.ErrorMessage ?? "Scheduled task enumeration failed.";
            return [];
        }

        var entries = new List<ScheduledTaskEntry>(enumerated.Value.Count);
        foreach (var info in enumerated.Value)
        {
            var overridden = _overrides.Get(info.Path);
            var (isCompanion, companionDescription) = ScheduledTaskClassifier.GetCompanionInfo(info);

            entries.Add(new ScheduledTaskEntry
            {
                Name = info.Name,
                Path = info.Path,
                Author = info.Author,
                Description = info.Description,
                TriggerTypes = info.TriggerTypes,
                LastRunTime = info.LastRunTime,
                LastTaskResult = info.LastTaskResult,
                IsEnabled = info.IsEnabled,
                Command = info.Command,
                Arguments = info.Arguments,
                WorkingDirectory = info.WorkingDirectory,
                ComHandlerClsid = info.ComHandlerClsid,
                Classification = overridden ?? ScheduledTaskClassifier.Classify(info),
                IsClassificationOverridden = overridden is not null,
                IsCompanionTask = isCompanion,
                CompanionDescription = companionDescription,
            });
        }

        return entries;
    }

    /// <summary>Projects logon/boot-triggered tasks into the Startup section's entry list (3-1 AC1).</summary>
    public static IReadOnlyList<StartupEntry> ToStartupEntries(IReadOnlyList<ScheduledTaskEntry> tasks) =>
        tasks.Where(t => t.IsStartupTask)
            .Select(t => new StartupEntry
            {
                Name = t.Name,
                Command = t.Path,
                ExecutablePath = null,
                Publisher = t.Author,
                Description = t.Description,
                Source = StartupSource.ScheduledTask,
                SourceLocation = t.Path,
                IsEnabled = t.IsEnabled,
            })
            .ToList();
}
