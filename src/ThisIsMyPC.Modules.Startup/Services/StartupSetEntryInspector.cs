using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Resolves set entries targeting "Startup &amp; Services" to live system state. SettingIds
/// are instance-scoped: "service-starttype:{serviceName}" (value = ServiceStartType name),
/// "scheduled-task:{taskPath}" (value = "Enabled"/"Disabled"), and
/// "startup-entry:{source}:{name}" (value = hex StartupApproved blob). A service, task, or
/// startup entry absent on this machine resolves to null; the set loader marks the entry
/// "will be skipped", which is the intended behavior for machine-specific targets.
/// </summary>
public sealed class StartupSetEntryInspector : ISetEntryInspector
{
    private const string ServicePrefix = "service-starttype:";
    private const string TaskPrefix = "scheduled-task:";
    private const string StartupEntryPrefix = "startup-entry:";

    private readonly IServiceControlService _serviceControl;
    private readonly IScheduledTaskService _taskService;
    private readonly IRegistryService _registry;
    private readonly StartupScanner _startupScanner;

    public StartupSetEntryInspector(
        IServiceControlService serviceControl,
        IScheduledTaskService taskService,
        IRegistryService registry,
        IStartupFolderService startupFolders)
    {
        _serviceControl = serviceControl;
        _taskService = taskService;
        _registry = registry;
        // Publisher/description metadata is irrelevant to state resolution; skip the
        // per-executable version-info reads the module UI pays for.
        _startupScanner = new StartupScanner(registry, startupFolders, _ => new StartupFileMetadata(null, null));
    }

    public string ModuleId => "Startup & Services";

    public SetEntryState? Inspect(SetEntry entry)
    {
        if (entry.SettingId.StartsWith(ServicePrefix, StringComparison.Ordinal))
            return InspectService(entry);
        if (entry.SettingId.StartsWith(TaskPrefix, StringComparison.Ordinal))
            return InspectTask(entry);
        if (entry.SettingId.StartsWith(StartupEntryPrefix, StringComparison.Ordinal))
            return InspectStartupEntry(entry);
        return null;
    }

    public ChangeGroup? CreateChangeGroup(SetEntry entry)
    {
        if (entry.SettingId.StartsWith(ServicePrefix, StringComparison.Ordinal))
            return CreateServiceGroup(entry);
        if (entry.SettingId.StartsWith(TaskPrefix, StringComparison.Ordinal))
            return CreateTaskGroup(entry);
        if (entry.SettingId.StartsWith(StartupEntryPrefix, StringComparison.Ordinal))
            return CreateStartupEntryGroup(entry);
        return null;
    }

    private SetEntryState? InspectService(SetEntry entry)
    {
        var query = _serviceControl.Query(entry.SettingId[ServicePrefix.Length..]);
        if (!query.IsSuccess || query.Value is null)
            return null;

        return new SetEntryState
        {
            SettingDisplayName = $"Service startup type: {query.Value.DisplayName}",
            CurrentValue = query.Value.StartType.ToString(),
            CurrentDisplay = ServiceChangeFactory.Describe(query.Value.StartType),
            IsApplied = string.Equals(query.Value.StartType.ToString(), entry.Value, StringComparison.Ordinal),
        };
    }

    private ChangeGroup? CreateServiceGroup(SetEntry entry)
    {
        if (ParseStartType(entry.Value) is not { } newStartType)
            return null;

        var query = _serviceControl.Query(entry.SettingId[ServicePrefix.Length..]);
        if (!query.IsSuccess || query.Value is null)
            return null;

        var serviceEntry = new ServiceEntry
        {
            ServiceName = query.Value.ServiceName,
            DisplayName = query.Value.DisplayName,
            State = query.Value.State,
            StartType = query.Value.StartType,
        };
        return Wrap(ServiceChangeFactory.CreateStartTypeChange(serviceEntry, newStartType), entry.Description);
    }

    private SetEntryState? InspectTask(SetEntry entry)
    {
        var query = _taskService.Query(entry.SettingId[TaskPrefix.Length..]);
        if (!query.IsSuccess || query.Value is null)
            return null;

        var current = query.Value.IsEnabled ? "Enabled" : "Disabled";
        return new SetEntryState
        {
            SettingDisplayName = $"Scheduled task: {query.Value.Name}",
            CurrentValue = current,
            CurrentDisplay = current,
            IsApplied = string.Equals(current, entry.Value, StringComparison.Ordinal),
        };
    }

    private ChangeGroup? CreateTaskGroup(SetEntry entry)
    {
        if (entry.Value is not ("Enabled" or "Disabled"))
            return null;

        var query = _taskService.Query(entry.SettingId[TaskPrefix.Length..]);
        if (!query.IsSuccess || query.Value is null)
            return null;

        // Use the entry's own path, not the COM-echoed one: Task Scheduler resolves paths
        // case-insensitively, and a differently-cased SettingId would break the resolver's
        // ordinal already-staged/conflict matching on the next preview.
        var taskEntry = new ScheduledTaskEntry
        {
            Name = query.Value.Name,
            Path = entry.SettingId[TaskPrefix.Length..],
            IsEnabled = query.Value.IsEnabled,
            Classification = TaskClassification.Unknown,
        };
        return Wrap(ScheduledTaskChangeFactory.CreateToggle(taskEntry, enable: entry.Value == "Enabled"), entry.Description);
    }

    private SetEntryState? InspectStartupEntry(SetEntry entry)
    {
        if (FindStartupEntry(entry.SettingId) is not { } startupEntry)
            return null;

        var current = startupEntry.IsEnabled ? "Enabled" : "Disabled";
        var wantsEnable = string.Equals(entry.Value, Convert.ToHexString(StartupChangeFactory.EnabledBlob), StringComparison.Ordinal);
        var wantsDisable = string.Equals(entry.Value, Convert.ToHexString(StartupChangeFactory.DisabledBlob), StringComparison.Ordinal);

        return new SetEntryState
        {
            SettingDisplayName = $"Startup entry: {startupEntry.Name}",
            CurrentValue = ReadApprovedBlob(startupEntry) is { } blob ? Convert.ToHexString(blob) : string.Empty,
            CurrentDisplay = current,
            IsApplied = wantsEnable ? startupEntry.IsEnabled : wantsDisable && !startupEntry.IsEnabled,
        };
    }

    private ChangeGroup? CreateStartupEntryGroup(SetEntry entry)
    {
        var enable = string.Equals(entry.Value, Convert.ToHexString(StartupChangeFactory.EnabledBlob), StringComparison.Ordinal) ? true
            : string.Equals(entry.Value, Convert.ToHexString(StartupChangeFactory.DisabledBlob), StringComparison.Ordinal) ? false
            : (bool?)null;
        if (enable is not { } direction || FindStartupEntry(entry.SettingId) is not { } startupEntry)
            return null;

        var change = StartupChangeFactory.CreateToggle(startupEntry, direction, ReadApprovedBlob(startupEntry));
        return change is null ? null : Wrap(change, entry.Description);
    }

    /// <summary>
    /// Matches "startup-entry:{Source}:{Name}" against the live scan, so entries whose
    /// underlying Run value or shortcut no longer exists resolve to null instead of
    /// staging an orphan StartupApproved write.
    /// </summary>
    private StartupEntry? FindStartupEntry(string settingId)
    {
        var parts = settingId[StartupEntryPrefix.Length..].Split(':', 2);
        if (parts.Length != 2 || !Enum.TryParse<StartupSource>(parts[0], out var source))
            return null;

        return _startupScanner.Scan().StartupEntries.FirstOrDefault(e =>
            e.Source == source && string.Equals(e.Name, parts[1], StringComparison.OrdinalIgnoreCase));
    }

    private byte[]? ReadApprovedBlob(StartupEntry startupEntry)
    {
        var approvedKey = StartupChangeFactory.GetApprovedKeyPath(startupEntry.Source);
        if (approvedKey is null)
            return null;
        var blob = _registry.ReadBinary(approvedKey, startupEntry.Name);
        return blob.IsSuccess ? blob.Value : null;
    }

    private static ServiceStartType? ParseStartType(string value) => value switch
    {
        "Automatic" => ServiceStartType.Automatic,
        "AutomaticDelayed" => ServiceStartType.AutomaticDelayed,
        "Manual" => ServiceStartType.Manual,
        "Disabled" => ServiceStartType.Disabled,
        _ => null,
    };

    private static ChangeGroup Wrap(ChangeDescriptor change, string description) => new()
    {
        GroupId = Guid.NewGuid().ToString("N"),
        DisplayName = change.DisplayName,
        Description = description,
        Changes = [change],
    };
}
