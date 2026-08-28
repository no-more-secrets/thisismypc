namespace ThisIsMyPC.Modules.Startup.Models;

/// <summary>
/// Aggregate scan result for the Startup &amp; Services module. Services and
/// scheduled-task lists are added by later Epic 3 stories.
/// </summary>
public sealed record StartupScanData(IReadOnlyList<StartupEntry> StartupEntries);
