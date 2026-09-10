using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Modules.Hardware.Models;

/// <summary>
/// What one Hardware tab shows: the policy's decision for its domain, the
/// full report it came from (identity and form factor are shared), where the
/// companion can be launched from, and the detection notes for the details
/// block. <see cref="ObservedAt"/> is the detection pass time, not the page
/// open. <see cref="RefreshInBackground"/> says the page opened on a cached
/// snapshot old enough to be worth re-checking behind it.
/// </summary>
public sealed record HardwareTabScanData(
    HardwareTabDecision Decision,
    HardwareCompatibilityReport Report,
    string? LaunchPath,
    IReadOnlyList<string> DetectionNotes,
    DateTimeOffset ObservedAt,
    bool RefreshInBackground = false);
