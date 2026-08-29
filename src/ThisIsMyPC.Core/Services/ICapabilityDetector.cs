using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Services;

public interface ICapabilityDetector
{
    bool IsAvailable(SystemCapability capability);
    ModuleAvailability GetAvailability(SystemCapability capability);

    /// <summary>
    /// Windows edition family detected at startup and cached for the session.
    /// Null when the edition could not be determined (unknown EditionID or registry read failure).
    /// </summary>
    WindowsSku? Sku { get; }

    /// <summary>
    /// Why <see cref="Sku"/> is null, for diagnostics and future callout UI. Null when detection succeeded.
    /// </summary>
    string? SkuDetectionFailureReason { get; }

    /// <summary>
    /// True when <paramref name="restriction"/> names the current edition family.
    /// An unknown SKU never triggers a restriction — callouts are informational only.
    /// </summary>
    bool IsSkuRestricted(WindowsSku? restriction);

    /// <summary>
    /// Whether the Owner Mode background service is installed and running. Drives the
    /// card degradation pattern (10.3). The availability-changed observable arrives
    /// with the service itself (Epic 28).
    /// </summary>
    bool IsOwnerModeAvailable { get; }

    /// <summary>
    /// One row per <see cref="SystemCapability"/>: availability plus friendly name and
    /// remediation, for the first-launch summary (5-2) and diagnostics.
    /// </summary>
    IReadOnlyList<CapabilityReportRow> GetCapabilityReport();
}

public sealed record CapabilityReportRow(
    SystemCapability Capability,
    string DisplayName,
    ModuleAvailability Availability);
