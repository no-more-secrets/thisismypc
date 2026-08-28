namespace ThisIsMyPC.Modules.Startup.Models;

/// <summary>
/// Aggregate scan result for the Startup &amp; Services module. The
/// scheduled-task list arrives with Story 3.4.
/// </summary>
public sealed record StartupScanData(
    IReadOnlyList<StartupEntry> StartupEntries,
    IReadOnlyList<ServiceEntry> Services,
    string? ServicesScanError = null);
