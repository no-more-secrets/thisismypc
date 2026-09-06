namespace ThisIsMyPC.Core.Hardware;

/// <summary>What kind of machine this is. Unknown is a real answer, never rounded to Desktop.</summary>
public enum MachineFormFactor
{
    Unknown,
    Desktop,
    Laptop,
}

/// <summary>
/// Mirror of the powrprof POWER_PLATFORM_ROLE values (PowerDeterminePlatformRoleEx).
/// Numeric values match the Win32 enum so an interop layer can cast directly.
/// </summary>
public enum PlatformRole
{
    Unspecified = 0,
    Desktop = 1,
    Mobile = 2,
    Workstation = 3,
    EnterpriseServer = 4,
    SohoServer = 5,
    AppliancePc = 6,
    PerformanceServer = 7,
    Slate = 8,
}

/// <summary>
/// Raw signals a detection layer can supply. Every field is optional; null means
/// "not observed". The classifier never treats an absent signal as evidence.
/// </summary>
public sealed record FormFactorEvidence
{
    public static FormFactorEvidence None { get; } = new();

    /// <summary>SMBIOS Type 3 (System Enclosure) chassis type codes, DMTF table "System Enclosure or Chassis Types".</summary>
    public IReadOnlyList<int>? SmbiosChassisTypes { get; init; }

    /// <summary>
    /// Result of PowerDeterminePlatformRoleEx, when read. Corroborating only: the
    /// API falls back to a battery-based guess when the ACPI FADT has no profile.
    /// </summary>
    public PlatformRole? PlatformRole { get; init; }

    /// <summary>GetSystemPowerStatus BatteryFlag reports a system battery (128 and 255 mean no or unknown).</summary>
    public bool? HasSystemBattery { get; init; }

    /// <summary>An internal display panel is attached (the Display module's laptop-panel path).</summary>
    public bool? HasInternalDisplayPanel { get; init; }
}

/// <summary>Classified form factor with the evidence trail behind it.</summary>
public sealed record FormFactorDecision(
    MachineFormFactor FormFactor,
    IReadOnlyList<string> Reasons);
