using System.Globalization;

namespace ThisIsMyPC.Core.Hardware;

/// <summary>
/// Deterministic desktop/laptop/unknown classification from supplied evidence.
/// Only a recognized SMBIOS chassis class establishes the form factor. The
/// power platform role (PowerDeterminePlatformRoleEx) reads the ACPI FADT
/// preferred profile and, when the FADT gives none, falls back to inferring
/// Mobile from battery presence (Microsoft docs for powerbase
/// PowerDeterminePlatformRoleEx). An API-only role can therefore be nothing more
/// than a battery guess, so it corroborates and is recorded as a reason, never
/// decides. A battery or an internal panel alone never makes a laptop either:
/// docked all-in-ones, UPS-backed desktops and firmware quirks report both.
/// </summary>
public static class FormFactorClassifier
{
    // DMTF SMBIOS Reference Specification, System Enclosure or Chassis Types.
    private static readonly HashSet<int> PortableChassis =
    [
        8,  // Portable
        9,  // Laptop
        10, // Notebook
        14, // Sub Notebook
        30, // Tablet
        31, // Convertible
        32, // Detachable
    ];

    private static readonly HashSet<int> StationaryChassis =
    [
        3,  // Desktop
        4,  // Low Profile Desktop
        5,  // Pizza Box
        6,  // Mini Tower
        7,  // Tower
        13, // All in One
        15, // Space-saving
        16, // Lunch Box
        17, // Main Server Chassis
        23, // Rack Mount Chassis
        24, // Sealed-case PC
        34, // Embedded PC
        35, // Mini PC
        36, // Stick PC
    ];

    public static FormFactorDecision Classify(FormFactorEvidence? evidence)
    {
        evidence ??= FormFactorEvidence.None;
        var reasons = new List<string>();

        var chassisVerdict = ClassifyChassis(evidence.SmbiosChassisTypes, reasons, out var chassisContradictory);
        var roleVerdict = ClassifyRole(evidence.PlatformRole, reasons);

        if (evidence.HasSystemBattery is { } battery)
            reasons.Add(battery ? "System battery present (corroborating only)." : "No system battery reported.");
        if (evidence.HasInternalDisplayPanel is { } panel)
            reasons.Add(panel ? "Internal display panel present (corroborating only)." : "No internal display panel.");

        if (chassisContradictory)
        {
            // Firmware that names both classes is unreliable; corroborating
            // signals cannot break the tie.
            reasons.Add("Contradictory chassis types leave the form factor unknown regardless of platform role.");
            return new FormFactorDecision(MachineFormFactor.Unknown, reasons);
        }

        if (chassisVerdict is not MachineFormFactor.Unknown)
        {
            if (roleVerdict is not MachineFormFactor.Unknown)
                reasons.Add(roleVerdict == chassisVerdict
                    ? "Platform role agrees with the chassis type (corroborating only)."
                    : "Platform role disagrees with the chassis type; chassis type wins.");
            return new FormFactorDecision(chassisVerdict, reasons);
        }

        if (roleVerdict is not MachineFormFactor.Unknown)
            reasons.Add("Platform role alone does not establish the form factor: without an ACPI FADT profile it is inferred from battery presence.");
        else if (evidence.HasSystemBattery is true || evidence.HasInternalDisplayPanel is true)
            reasons.Add("Battery or internal panel alone is not treated as proof of a laptop.");
        else if (reasons.Count == 0)
            reasons.Add("No form-factor evidence supplied.");

        return new FormFactorDecision(MachineFormFactor.Unknown, reasons);
    }

    private static MachineFormFactor ClassifyChassis(
        IReadOnlyList<int>? chassisTypes, List<string> reasons, out bool contradictory)
    {
        contradictory = false;
        if (chassisTypes is null || chassisTypes.Count == 0)
            return MachineFormFactor.Unknown;

        var portable = chassisTypes.Any(PortableChassis.Contains);
        var stationary = chassisTypes.Any(StationaryChassis.Contains);
        var codes = string.Join(", ", chassisTypes.Select(c => c.ToString(CultureInfo.InvariantCulture)));

        if (portable && stationary)
        {
            contradictory = true;
            reasons.Add($"SMBIOS chassis types {codes} name both portable and stationary enclosures.");
            return MachineFormFactor.Unknown;
        }

        if (portable)
        {
            reasons.Add($"SMBIOS chassis type {codes} is a portable enclosure.");
            return MachineFormFactor.Laptop;
        }

        if (stationary)
        {
            reasons.Add($"SMBIOS chassis type {codes} is a stationary enclosure.");
            return MachineFormFactor.Desktop;
        }

        reasons.Add($"SMBIOS chassis type {codes} does not identify the enclosure.");
        return MachineFormFactor.Unknown;
    }

    private static MachineFormFactor ClassifyRole(PlatformRole? role, List<string> reasons)
    {
        switch (role)
        {
            case null:
                return MachineFormFactor.Unknown;
            case PlatformRole.Mobile:
            case PlatformRole.Slate:
                reasons.Add($"Power platform role is {role}.");
                return MachineFormFactor.Laptop;
            case PlatformRole.Desktop:
            case PlatformRole.Workstation:
            case PlatformRole.EnterpriseServer:
            case PlatformRole.SohoServer:
            case PlatformRole.AppliancePc:
            case PlatformRole.PerformanceServer:
                reasons.Add($"Power platform role is {role}.");
                return MachineFormFactor.Desktop;
            default:
                reasons.Add("Power platform role is unspecified.");
                return MachineFormFactor.Unknown;
        }
    }
}
