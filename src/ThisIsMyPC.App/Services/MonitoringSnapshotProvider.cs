using ThisIsMyPC.Core.Monitoring;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Captures the boot-sequence surface for monitoring (9-3): startup entries (run
/// keys + startup folders), services, and scheduled tasks. Item ids reuse the
/// Startup &amp; Services set settingId conventions so detections can be disabled
/// through the existing inspector. Enumeration failures degrade to omissions —
/// monitoring is best-effort, never a crash source.
/// </summary>
public sealed class MonitoringSnapshotProvider : IMonitoringSnapshotProvider
{
    private readonly StartupScanner _startupScanner;
    private readonly IServiceControlService _serviceControl;
    private readonly IScheduledTaskService _taskService;

    public MonitoringSnapshotProvider(
        IRegistryService registry,
        IStartupFolderService startupFolders,
        IServiceControlService serviceControl,
        IScheduledTaskService taskService)
    {
        // Metadata reads (publisher/version) are irrelevant to presence detection.
        _startupScanner = new StartupScanner(registry, startupFolders, _ => new StartupFileMetadata(null, null));
        _serviceControl = serviceControl;
        _taskService = taskService;
    }

    public IReadOnlyList<MonitorItem> Capture()
    {
        var items = new List<MonitorItem>();

        try
        {
            foreach (var entry in _startupScanner.Scan().StartupEntries)
            {
                items.Add(new MonitorItem(
                    StartupChangeFactory.GetSettingId(entry),
                    entry.Name,
                    $"startup ({entry.Source})"));
            }
        }
#pragma warning disable CA1031 // best-effort capture — a failing source is omitted, never fatal
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Monitoring: startup entry scan failed");
        }
#pragma warning restore CA1031

        if (_serviceControl.EnumerateAll() is { IsSuccess: true, Value: { } services })
        {
            foreach (var service in services)
                items.Add(new MonitorItem($"service-starttype:{service.ServiceName}", service.DisplayName, "service"));
        }

        if (_taskService.EnumerateAll() is { IsSuccess: true, Value: { } tasks })
        {
            foreach (var task in tasks)
                items.Add(new MonitorItem($"scheduled-task:{task.Path}", task.Name, "scheduled task"));
        }

        return items;
    }
}
