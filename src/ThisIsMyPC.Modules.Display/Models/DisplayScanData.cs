using ThisIsMyPC.Core.Display;

namespace ThisIsMyPC.Modules.Display.Models;

/// <summary>
/// Scan result: external DDC monitors plus, on machines with a battery, the
/// internal panel as a synthetic entry driven by the power plan. ScanError is
/// set when DDC enumeration failed entirely; the panel can still be present.
/// </summary>
public sealed record DisplayScanData(
    IReadOnlyList<MonitorDevice> Monitors,
    string? ScanError);
