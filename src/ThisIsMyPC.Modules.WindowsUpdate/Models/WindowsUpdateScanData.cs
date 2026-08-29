namespace ThisIsMyPC.Modules.WindowsUpdate.Models;

/// <summary>
/// Scan snapshot for the Windows Update module. <see cref="VersionPin"/> is the
/// three-value group (TargetReleaseVersion first — its "1" is the group's toggle value);
/// empty when the machine's DisplayVersion could not be read (the pin is then
/// unavailable rather than pinning to nothing).
/// </summary>
public sealed record WindowsUpdateScanData(
    IReadOnlyList<UpdatePolicySetting> Settings,
    IReadOnlyList<UpdatePolicySetting> VersionPin,
    IReadOnlyList<UpdatePolicySetting> UxSettings);
