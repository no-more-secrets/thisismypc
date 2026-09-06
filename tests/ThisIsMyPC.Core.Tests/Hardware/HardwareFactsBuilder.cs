using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Core.Tests.Hardware;

/// <summary>
/// Fake fact sets for the decision matrix. No live probing anywhere. Every
/// machine helper records a confirmed NotInstalled for each companion detection
/// did not name, so the matrix models "detection ran". Use
/// <see cref="Unobserved"/> for "detection never looked".
/// </summary>
internal static class HardwareFactsBuilder
{
    public static readonly FormFactorEvidence LaptopEvidence = new()
    {
        SmbiosChassisTypes = [10],
        PlatformRole = PlatformRole.Mobile,
        HasSystemBattery = true,
        HasInternalDisplayPanel = true,
    };

    public static readonly FormFactorEvidence DesktopEvidence = new()
    {
        SmbiosChassisTypes = [3],
        PlatformRole = PlatformRole.Desktop,
        HasSystemBattery = false,
        HasInternalDisplayPanel = false,
    };

    public static ObservedHardwareFacts AsusLaptop(
        bool atkacpi = true,
        params CompanionObservation[] companions) => new()
    {
        Identity = MachineIdentity.From("ASUSTeK COMPUTER INC.", "ROG Strix G614JZ_G614JZ"),
        FormFactor = LaptopEvidence,
        AsusPlatformDriverPresent = atkacpi,
        Companions = WithAbsentDefaults(companions),
    };

    public static ObservedHardwareFacts OtherLaptop(string manufacturer = "LENOVO", params CompanionObservation[] companions) => new()
    {
        Identity = MachineIdentity.From(manufacturer, "82WK"),
        FormFactor = LaptopEvidence,
        AsusPlatformDriverPresent = false,
        Companions = WithAbsentDefaults(companions),
    };

    public static ObservedHardwareFacts Desktop(string manufacturer = "ASUSTeK COMPUTER INC.", params CompanionObservation[] companions) => new()
    {
        Identity = MachineIdentity.From(manufacturer, "ROG STRIX X670E-E GAMING WIFI"),
        FormFactor = DesktopEvidence,
        AsusPlatformDriverPresent = false,
        Companions = WithAbsentDefaults(companions),
    };

    public static ObservedHardwareFacts UnknownMachine(params CompanionObservation[] companions) => new()
    {
        Identity = MachineIdentity.From("To Be Filled By O.E.M.", "To Be Filled By O.E.M."),
        FormFactor = FormFactorEvidence.None,
        Companions = WithAbsentDefaults(companions),
    };

    /// <summary>Same machine, but companion detection never ran.</summary>
    public static ObservedHardwareFacts Unobserved(ObservedHardwareFacts facts) => facts with { Companions = [] };

    private static IReadOnlyList<CompanionObservation> WithAbsentDefaults(CompanionObservation[] explicitObservations)
    {
        var list = new List<CompanionObservation>(explicitObservations);
        foreach (var app in Enum.GetValues<CompanionApp>())
        {
            if (list.All(o => o.App != app))
                list.Add(CompanionObservation.NotInstalled(app));
        }
        return list;
    }
}
