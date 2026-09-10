using ThisIsMyPC.Core.Hardware.Lighting;

namespace ThisIsMyPC.Core.Hardware;

/// <summary>State of the in-process sensor backend the Monitoring tab will use.</summary>
public enum SensorBackendState
{
    /// <summary>No backend is wired yet (this batch).</summary>
    NotIntegrated,

    /// <summary>Backend present but its kernel driver (PawnIO) is not loaded.</summary>
    DriverMissing,

    /// <summary>Backend can enumerate sensors.</summary>
    Ready,
}

/// <summary>
/// Everything the compatibility policy reasons over. A detection layer fills
/// this from live sources; tests build it by hand. The record carries no
/// behavior so it can be logged, compared and serialized as-is.
/// </summary>
public sealed record ObservedHardwareFacts
{
    public static ObservedHardwareFacts Empty { get; } = new();

    public MachineIdentity Identity { get; init; } = MachineIdentity.Unknown;

    public FormFactorEvidence FormFactor { get; init; } = FormFactorEvidence.None;

    /// <summary>
    /// Companions detection looked for. A companion with no entry was not checked,
    /// which is Unknown, not absent; use <see cref="CompanionObservation.NotInstalled"/>
    /// to record a confirmed absence.
    /// </summary>
    public IReadOnlyList<CompanionObservation> Companions { get; init; } = [];

    /// <summary>ASUS ATKACPI platform driver observed (SystemCapability.AsusAtkacpi). Null when not probed.</summary>
    public bool? AsusPlatformDriverPresent { get; init; }

    /// <summary>
    /// Devices the built-in lighting controllers found (docs/lighting-controllers.md).
    /// Empty when detection ran and found none; null when it did not run.
    /// </summary>
    public IReadOnlyList<LightingDeviceSummary>? LightingDevices { get; init; }

    /// <summary>OpenRGB SDK server answered on its socket. Null when not probed. Only ownership evidence now: the tab drives devices itself.</summary>
    public bool? OpenRgbServerReachable { get; init; }

    /// <summary>Devices the OpenRGB server enumerated. Null when the server was not queried.</summary>
    public int? OpenRgbDeviceCount { get; init; }

    public SensorBackendState SensorBackend { get; init; } = SensorBackendState.NotIntegrated;

    public CompanionObservation? Companion(CompanionApp app)
        => Companions.FirstOrDefault(c => c.App == app);

    /// <summary>Installed, not installed, or null when detection never looked.</summary>
    public bool? IsInstalled(CompanionApp app) => Companion(app)?.IsInstalled;

    /// <summary>Running, not running, or null when detection never looked.</summary>
    public bool? IsRunning(CompanionApp app) => Companion(app)?.IsRunning;
}
