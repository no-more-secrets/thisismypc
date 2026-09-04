using ThisIsMyPC.Core.Display;

namespace ThisIsMyPC.Modules.Display.Models;

/// <summary>
/// Scan result: external DDC monitors plus, on machines with a battery, the
/// internal panel as a synthetic entry driven by the power plan. ScanError is
/// set when DDC enumeration failed entirely; the panel can still be present.
/// IsPartial marks a quick scan: at least one monitor's feature list is still
/// pending a full scan, so its input and vendor rows are not there yet.
/// </summary>
public sealed record DisplayScanData(
    IReadOnlyList<MonitorDevice> Monitors,
    string? ScanError,
    bool IsPartial = false);
