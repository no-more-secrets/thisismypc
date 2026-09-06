using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Hardware;

/// <summary>Outcome for one Hardware tab. Every tab stays visible; this says what it can do.</summary>
public enum HardwareAvailability
{
    /// <summary>The tab's backend or companion applies to this machine. Which operations it may run is a separate field.</summary>
    Available,

    /// <summary>Known not to apply here (a desktop on System Control, no backend installed).</summary>
    Unavailable,

    /// <summary>Machine, vendor, support or a companion could not be determined. Never treated as compatible.</summary>
    Unknown,

    /// <summary>The backend could work, but its support for this machine has not been verified.</summary>
    PendingVerification,

    /// <summary>Another running program is known to own these devices; live writes are refused.</summary>
    Conflict,
}

/// <summary>
/// Operations a tab is permitted to run. Availability says a backend applies;
/// this says what the tab may actually do with it. Opening or installing a
/// companion never implies writing to hardware, and reading sensors never does.
/// </summary>
[Flags]
public enum HardwareOperations
{
    None = 0,

    /// <summary>Offer the companion install through the Software queue.</summary>
    InstallCompanion = 1,

    /// <summary>Launch the companion as the desktop user.</summary>
    OpenCompanion = 2,

    /// <summary>Read sensor values; no device state changes.</summary>
    ReadSensors = 4,

    /// <summary>Change device state through the backend (lighting colors, modes).</summary>
    WriteDevices = 8,
}

/// <summary>A companion-app button the tab can offer.</summary>
public enum CompanionActionKind
{
    Install,
    Open,
}

public sealed record CompanionAction(CompanionActionKind Kind, CompanionApp App);

/// <summary>
/// Per-tab decision. <see cref="ControlsVisible"/> honors the debug override;
/// <see cref="Operations"/> and <see cref="LiveWritesAllowed"/> never do. A view
/// that shows controls because of the override must still check the permitted
/// operations before touching hardware.
/// </summary>
public sealed record HardwareTabDecision
{
    public required HardwareDomain Domain { get; init; }
    public required HardwareAvailability Availability { get; init; }

    /// <summary>Backend or companion the tab would use. Null when nothing applies.</summary>
    public CompanionApp? Backend { get; init; }

    /// <summary>One or two sentences of product copy telling the person what the tab can do here and why.</summary>
    public required string Explanation { get; init; }

    /// <summary>Evidence lines behind the decision, for the tab's details disclosure and logs.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>Running programs that own these devices or may interfere, with the reason.</summary>
    public IReadOnlyList<string> ConflictNotes { get; init; } = [];

    public CompanionAction? Action { get; init; }

    /// <summary>Operations the tab may run. Granted only by the policy; the debug override cannot add any.</summary>
    public required HardwareOperations Operations { get; init; }

    /// <summary>True only when <see cref="Operations"/> includes <see cref="HardwareOperations.WriteDevices"/>.</summary>
    public bool LiveWritesAllowed => Operations.HasFlag(HardwareOperations.WriteDevices);

    /// <summary>Whether the tab renders its controls. True when available, or when the debug override is on.</summary>
    public required bool ControlsVisible { get; init; }

    /// <summary>Bridge to the existing module gating record. Available means the backend applies, not that writes are allowed.</summary>
    public ModuleAvailability ToModuleAvailability() => new(
        Availability == HardwareAvailability.Available,
        Availability == HardwareAvailability.Available ? null : Explanation,
        Action is { } action ? DescribeAction(action) : null);

    private static string DescribeAction(CompanionAction action) => action.Kind switch
    {
        CompanionActionKind.Install => $"Install {CompanionNames.Of(action.App)} from Software.",
        CompanionActionKind.Open => $"Open {CompanionNames.Of(action.App)}.",
        _ => string.Empty,
    };
}

/// <summary>Result for all domains plus the shared identity conclusions.</summary>
public sealed record HardwareCompatibilityReport
{
    public required MachineIdentity Identity { get; init; }
    public required FormFactorDecision FormFactor { get; init; }
    public required IReadOnlyList<HardwareTabDecision> Tabs { get; init; }
    public required bool VisibilityOverrideActive { get; init; }

    public HardwareTabDecision For(HardwareDomain domain) => Tabs.First(t => t.Domain == domain);
}

public static class CompanionNames
{
    public static string Of(CompanionApp app) => app switch
    {
        CompanionApp.GHelper => "G-Helper",
        CompanionApp.ArmouryCrate => "Armoury Crate",
        CompanionApp.FanControl => "FanControl",
        CompanionApp.OpenRgb => "OpenRGB",
        CompanionApp.SignalRgb => "SignalRGB",
        CompanionApp.LibreHardwareMonitor => "LibreHardwareMonitor",
        CompanionApp.HwInfo => "HWiNFO",
        _ => app.ToString(),
    };
}
