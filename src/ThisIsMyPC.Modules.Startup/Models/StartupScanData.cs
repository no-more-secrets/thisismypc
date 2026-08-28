namespace ThisIsMyPC.Modules.Startup.Models;

/// <summary>Aggregate scan result for the Startup &amp; Services module.</summary>
public sealed record StartupScanData(
    IReadOnlyList<StartupEntry> StartupEntries,
    IReadOnlyList<ServiceEntry> Services,
    string? ServicesScanError = null,
    IReadOnlyList<ScheduledTaskEntry>? ScheduledTasks = null,
    string? ScheduledTasksScanError = null)
{
    public IReadOnlyList<ScheduledTaskEntry> ScheduledTasks { get; init; } = ScheduledTasks ?? [];
}
