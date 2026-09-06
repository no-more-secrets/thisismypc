namespace ThisIsMyPC.Core.Hardware;

/// <summary>Third-party hardware software the policy knows by name.</summary>
public enum CompanionApp
{
    /// <summary>seerge/G-Helper: community ASUS laptop control, the intended System Control companion.</summary>
    GHelper,

    /// <summary>ASUS Armoury Crate and its services.</summary>
    ArmouryCrate,

    /// <summary>Rem0o/FanControl, the intended Cooling companion.</summary>
    FanControl,

    /// <summary>OpenRGB, the Lighting backend (reached over its SDK server).</summary>
    OpenRgb,

    /// <summary>SignalRGB, a competing lighting controller.</summary>
    SignalRgb,

    /// <summary>LibreHardwareMonitor running as a separate application.</summary>
    LibreHardwareMonitor,

    /// <summary>HWiNFO running as a separate application.</summary>
    HwInfo,
}

/// <summary>
/// What a detection layer saw of one companion. Installed and running are
/// separate facts; neither implies the other. Ownership is a third fact: the
/// domains the companion is known to be driving right now, when detection can
/// tell (an OpenRGB server answering with devices, a FanControl configuration
/// that is loaded). An empty list with IsRunning true means "running, ownership
/// not observed", and the policy treats that as advisory, never as a conflict.
/// </summary>
public sealed record CompanionObservation
{
    public CompanionObservation(CompanionApp app, bool isInstalled, bool isRunning,
        IReadOnlyList<HardwareDomain>? observedOwnership = null)
    {
        App = app;
        IsInstalled = isInstalled;
        IsRunning = isRunning;
        ObservedOwnership = observedOwnership ?? [];
    }

    public CompanionApp App { get; }
    public bool IsInstalled { get; }
    public bool IsRunning { get; }
    public IReadOnlyList<HardwareDomain> ObservedOwnership { get; }

    /// <summary>Detection looked and found nothing. Distinct from no observation at all.</summary>
    public static CompanionObservation NotInstalled(CompanionApp app) => new(app, false, false);

    public static CompanionObservation Installed(CompanionApp app) => new(app, true, false);

    public static CompanionObservation Running(CompanionApp app, params HardwareDomain[] owns)
        => new(app, true, true, owns);
}
