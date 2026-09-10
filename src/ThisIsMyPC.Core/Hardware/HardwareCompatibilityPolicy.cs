using System.Globalization;

namespace ThisIsMyPC.Core.Hardware;

/// <summary>
/// Settings the policy takes from the app. <see cref="ShowAllControls"/> is the
/// Settings &gt; Advanced debug override: it makes every tab render its controls,
/// and nothing else. Availability, conflicts, actions and permitted operations
/// are computed without looking at it.
/// </summary>
public sealed record HardwareCompatibilityOptions(bool ShowAllControls = false)
{
    public static HardwareCompatibilityOptions Default { get; } = new();
}

/// <summary>
/// Deterministic compatibility decisions for the Hardware tabs from supplied
/// facts. Pure: no probing, no registry, no processes. The rules are the small
/// evidence-backed set in docs/planning/hardware-compatibility.md; anything the
/// facts do not establish comes out Unknown or PendingVerification, never
/// Available, and a companion that detection never looked for is Unknown, not
/// absent, so no install is offered before detection ran.
/// </summary>
public static class HardwareCompatibilityPolicy
{
    // Running programs that commonly touch a domain's devices even when detection
    // cannot see ownership. They produce an advisory note, not a Conflict.
    private static readonly Dictionary<HardwareDomain, CompanionApp[]> LikelyInterferers = new()
    {
        [HardwareDomain.SystemControl] = [CompanionApp.ArmouryCrate],
        [HardwareDomain.Lighting] = [CompanionApp.SignalRgb, CompanionApp.ArmouryCrate, CompanionApp.GHelper],
        [HardwareDomain.Cooling] = [CompanionApp.GHelper, CompanionApp.ArmouryCrate],
        [HardwareDomain.Monitoring] = [],
    };

    private static readonly Dictionary<HardwareDomain, CompanionApp> Backends = new()
    {
        [HardwareDomain.SystemControl] = CompanionApp.GHelper,
        [HardwareDomain.Lighting] = CompanionApp.OpenRgb,
        [HardwareDomain.Cooling] = CompanionApp.FanControl,
        [HardwareDomain.Monitoring] = CompanionApp.LibreHardwareMonitor,
    };

    public static HardwareCompatibilityReport Decide(ObservedHardwareFacts facts, HardwareCompatibilityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(facts);
        options ??= HardwareCompatibilityOptions.Default;

        var formFactor = FormFactorClassifier.Classify(facts.FormFactor);
        var tabs = new List<HardwareTabDecision>
        {
            DecideSystemControl(facts, formFactor, options),
            DecideLighting(facts, formFactor, options),
            DecideCooling(facts, formFactor, options),
            DecideMonitoring(facts, options),
        };

        return new HardwareCompatibilityReport
        {
            Identity = facts.Identity,
            FormFactor = formFactor,
            Tabs = tabs,
            VisibilityOverrideActive = options.ShowAllControls,
        };
    }

    /// <summary>Working state for one tab before the shared conflict pass.</summary>
    private sealed record Draft(HardwareAvailability Availability, string Explanation, CompanionAction? Action);

    private static HardwareTabDecision DecideSystemControl(
        ObservedHardwareFacts facts, FormFactorDecision formFactor, HardwareCompatibilityOptions options)
    {
        var evidence = IdentityEvidence(facts, formFactor);
        var verdict = GHelperSupportCatalog.Evaluate(facts, formFactor.FormFactor);
        evidence.Add($"G-Helper support verdict: {verdict}.");
        evidence.Add(DescribePresence(facts.Companion(CompanionApp.GHelper), CompanionApp.GHelper));
        if (facts.IsRunning(CompanionApp.GHelper) is true)
            evidence.Add("A running G-Helper is not taken as proof of support; it starts on unsupported machines too.");
        var vendorName = facts.Identity.Manufacturer ?? "this manufacturer";

        var draft = verdict switch
        {
            SupportVerdict.Unknown when facts.Identity.Vendor == MachineVendor.Unknown => new Draft(
                HardwareAvailability.Unknown,
                "The manufacturer of this PC could not be read, so no laptop control software can be matched to it.",
                null),
            SupportVerdict.Unknown when formFactor.FormFactor == MachineFormFactor.Unknown => new Draft(
                HardwareAvailability.Unknown,
                "Whether this PC is a laptop could not be determined. System Control needs that answer before it offers anything.",
                null),
            SupportVerdict.Unknown => new Draft(
                HardwareAvailability.Unknown,
                "The ASUS platform driver was not checked, so G-Helper support is unknown on this laptop.",
                OpenIfInstalled(facts, CompanionApp.GHelper)),
            SupportVerdict.Unsupported when formFactor.FormFactor == MachineFormFactor.Desktop => new Draft(
                HardwareAvailability.Unavailable,
                "System Control covers laptop performance modes, battery limits and keyboard controls. This PC is a desktop.",
                null),
            SupportVerdict.Unsupported when facts.Identity.Vendor == MachineVendor.Asus => new Draft(
                HardwareAvailability.Unavailable,
                "This ASUS laptop does not report the ATKACPI platform driver that G-Helper relies on.",
                null),
            SupportVerdict.Unsupported => new Draft(
                HardwareAvailability.Unavailable,
                $"No verified control software for {vendorName} laptops is offered yet.",
                null),
            SupportVerdict.Unverified => new Draft(
                HardwareAvailability.PendingVerification,
                "G-Helper targets ASUS laptops on this platform, but support for this model has not been verified. If you already use it, open it here.",
                OpenIfInstalled(facts, CompanionApp.GHelper)),
            SupportVerdict.Verified => new Draft(
                HardwareAvailability.Available,
                "G-Helper runs this laptop's performance modes, fan curves, battery limit and keyboard controls. ThisIsMyPC installs and opens it; it does not replace it.",
                OpenOrInstall(facts, CompanionApp.GHelper)),
            _ => new Draft(HardwareAvailability.Unknown, "System Control could not be evaluated.", null),
        };

        // System Control never writes hardware itself in this design; it only
        // launches or installs the companion.
        return Finish(HardwareDomain.SystemControl, draft, evidence, facts, options,
            backend: verdict is SupportVerdict.Verified or SupportVerdict.Unverified ? CompanionApp.GHelper : null,
            operationsWhenAvailable: HardwareOperations.None);
    }

    private static HardwareTabDecision DecideLighting(
        ObservedHardwareFacts facts, FormFactorDecision formFactor, HardwareCompatibilityOptions options)
    {
        var evidence = IdentityEvidence(facts, formFactor);
        var openRgb = facts.Companion(CompanionApp.OpenRgb);
        evidence.Add(DescribePresence(openRgb, CompanionApp.OpenRgb));
        if (formFactor.FormFactor == MachineFormFactor.Laptop)
            evidence.Add("Laptop: lighting depends on what OpenRGB detects; laptops are not excluded.");

        var bundled = facts.OpenRgbBundled is true;
        if (bundled)
            evidence.Add("A bundled OpenRGB ships with this app and runs as a background lighting service.");

        Draft draft;
        if (openRgb is null)
        {
            draft = new Draft(HardwareAvailability.Unknown,
                "OpenRGB was not checked for yet, so Lighting cannot say what it can do here.", null);
        }
        else if (!openRgb.IsInstalled)
        {
            draft = new Draft(HardwareAvailability.Unavailable,
                "Lighting works through OpenRGB, which is not installed.",
                new CompanionAction(CompanionActionKind.Install, CompanionApp.OpenRgb));
        }
        else if (!openRgb.IsRunning)
        {
            // With a bundled copy nothing is open to talk to, so the action
            // starts the background service; the page does that as it opens.
            // A user's own OpenRGB that is running without its SDK server is
            // never doubled up (that case is below): two servers would fight
            // for the same devices.
            draft = bundled
                ? new Draft(HardwareAvailability.Unavailable,
                    "The lighting service is not running. It starts when this page opens.",
                    new CompanionAction(CompanionActionKind.StartService, CompanionApp.OpenRgb))
                : new Draft(HardwareAvailability.Unavailable,
                    "OpenRGB is installed but not running. Start it with its SDK server enabled.",
                    new CompanionAction(CompanionActionKind.Open, CompanionApp.OpenRgb));
        }
        else
        {
            draft = facts.OpenRgbServerReachable switch
            {
                null => new Draft(HardwareAvailability.Unknown,
                    "OpenRGB is running, but its SDK server was not checked.", null),
                false => new Draft(HardwareAvailability.Unavailable,
                    "OpenRGB is running, but its SDK server is not answering. Enable the SDK server in OpenRGB settings.",
                    new CompanionAction(CompanionActionKind.Open, CompanionApp.OpenRgb)),
                true => facts.OpenRgbDeviceCount switch
                {
                    null => new Draft(HardwareAvailability.Unknown,
                        "OpenRGB's server answered, but its device list was not read yet.", null),
                    0 => new Draft(HardwareAvailability.Unavailable,
                        "OpenRGB is running but found no controllable lighting devices on this PC.", null),
                    // The bundled service has no window to open; the device cards are the controls.
                    > 0 => new Draft(HardwareAvailability.Available,
                        "Lighting is controlled through the running OpenRGB server.",
                        bundled ? null : new CompanionAction(CompanionActionKind.Open, CompanionApp.OpenRgb)),
                    _ => new Draft(HardwareAvailability.Unknown,
                        "OpenRGB's server returned an invalid device count, so Lighting cannot trust it.", null),
                },
            };
            if (facts.OpenRgbServerReachable is null)
                evidence.Add("OpenRGB SDK server reachability not probed.");
            else if (facts.OpenRgbServerReachable is true)
                evidence.Add(facts.OpenRgbDeviceCount switch
                {
                    null => "OpenRGB device count not queried.",
                    < 0 => $"OpenRGB device count invalid: {facts.OpenRgbDeviceCount.Value.ToString(CultureInfo.InvariantCulture)}.",
                    var count => $"OpenRGB device count: {count.Value.ToString(CultureInfo.InvariantCulture)}.",
                });
        }

        // Lighting is the one tab that writes devices itself, through the server.
        return Finish(HardwareDomain.Lighting, draft, evidence, facts, options,
            backend: CompanionApp.OpenRgb,
            operationsWhenAvailable: HardwareOperations.WriteDevices);
    }

    private static HardwareTabDecision DecideCooling(
        ObservedHardwareFacts facts, FormFactorDecision formFactor, HardwareCompatibilityOptions options)
    {
        var evidence = IdentityEvidence(facts, formFactor);
        var fanControl = facts.Companion(CompanionApp.FanControl);
        evidence.Add(DescribePresence(fanControl, CompanionApp.FanControl));

        Draft draft;
        if (fanControl is null)
        {
            draft = new Draft(HardwareAvailability.Unknown,
                "FanControl was not checked for yet, so Cooling cannot say what it can do here.", null);
        }
        else if (fanControl.IsInstalled)
        {
            // Running says nothing about whether FanControl has a configuration
            // loaded or drives any fan; ownership is a separate observation.
            draft = new Draft(HardwareAvailability.Available,
                fanControl.IsRunning
                    ? "FanControl is running. Open it to manage fan curves; ThisIsMyPC does not replace it."
                    : "FanControl is installed. Open it to manage fan curves; ThisIsMyPC does not replace it.",
                new CompanionAction(CompanionActionKind.Open, CompanionApp.FanControl));
            if (fanControl.IsRunning && fanControl.ObservedOwnership.Contains(HardwareDomain.Cooling))
                evidence.Add("FanControl was observed with a configuration loaded.");
        }
        else
        {
            draft = new Draft(HardwareAvailability.Unavailable,
                "Cooling works through FanControl, which is not installed.",
                new CompanionAction(CompanionActionKind.Install, CompanionApp.FanControl));
        }

        if (formFactor.FormFactor == MachineFormFactor.Laptop && facts.Identity.Vendor == MachineVendor.Asus)
            evidence.Add("ASUS laptop: fan modes are also available through G-Helper on System Control.");

        // Cooling never touches fans itself; FanControl does.
        return Finish(HardwareDomain.Cooling, draft, evidence, facts, options,
            backend: CompanionApp.FanControl,
            operationsWhenAvailable: HardwareOperations.None);
    }

    private static HardwareTabDecision DecideMonitoring(ObservedHardwareFacts facts, HardwareCompatibilityOptions options)
    {
        var evidence = new List<string> { $"Sensor backend state: {facts.SensorBackend}." };

        var draft = facts.SensorBackend switch
        {
            SensorBackendState.Ready => new Draft(HardwareAvailability.Available,
                "Sensors are read through LibreHardwareMonitor.", null),
            SensorBackendState.DriverMissing => new Draft(HardwareAvailability.Unavailable,
                "The sensor driver (PawnIO) is not loaded, so most readings are unavailable.", null),
            _ => new Draft(HardwareAvailability.PendingVerification,
                "Sensor readout is not wired up yet in this build.", null),
        };

        // Monitoring reads; it never writes.
        return Finish(HardwareDomain.Monitoring, draft, evidence, facts, options,
            backend: CompanionApp.LibreHardwareMonitor,
            operationsWhenAvailable: HardwareOperations.ReadSensors);
    }

    /// <summary>
    /// Applies conflict rules, grants operations, and applies the visibility
    /// override uniformly. A running companion whose observed ownership includes
    /// the domain (and that is not the domain's own backend) turns the decision
    /// into Conflict with no operations. Running likely interferers without
    /// observed ownership add an advisory note only. Installed-but-not-running
    /// programs add an evidence line; unobserved programs add nothing.
    /// Operations: the action grants Install or Open; <paramref name="operationsWhenAvailable"/>
    /// is added only when the tab is Available.
    /// </summary>
    private static HardwareTabDecision Finish(
        HardwareDomain domain,
        Draft draft,
        List<string> evidence,
        ObservedHardwareFacts facts,
        HardwareCompatibilityOptions options,
        CompanionApp? backend,
        HardwareOperations operationsWhenAvailable)
    {
        var backendApp = Backends[domain];
        var conflicts = new List<string>();
        var owners = facts.Companions
            .Where(c => c.IsRunning && c.App != backendApp && c.ObservedOwnership.Contains(domain))
            .ToList();

        foreach (var owner in owners)
            conflicts.Add($"{CompanionNames.Of(owner.App)} is running and currently controls these devices.");

        foreach (var app in LikelyInterferers[domain])
        {
            if (owners.Any(o => o.App == app) || app == backendApp)
                continue;
            if (facts.IsRunning(app) is true)
                conflicts.Add($"{CompanionNames.Of(app)} is running and may also drive these devices; ownership was not observed.");
            else if (facts.IsInstalled(app) is true)
                evidence.Add($"{CompanionNames.Of(app)} is installed but not running.");
        }

        var (availability, explanation, action) = draft;
        if (owners.Count > 0)
        {
            availability = HardwareAvailability.Conflict;
            explanation = $"{Join(owners.Select(o => CompanionNames.Of(o.App)))} currently controls these devices. Close it, or manage them there, before using this tab.";
            action = null;
        }

        var operations = action?.Kind switch
        {
            CompanionActionKind.Install => HardwareOperations.InstallCompanion,
            CompanionActionKind.Open => HardwareOperations.OpenCompanion,
            CompanionActionKind.StartService => HardwareOperations.OpenCompanion,
            _ => HardwareOperations.None,
        };
        if (availability == HardwareAvailability.Available)
            operations |= operationsWhenAvailable;

        return new HardwareTabDecision
        {
            Domain = domain,
            Availability = availability,
            Backend = backend,
            Explanation = explanation,
            Evidence = evidence,
            ConflictNotes = conflicts,
            Action = action,
            Operations = operations,
            ControlsVisible = availability == HardwareAvailability.Available || options.ShowAllControls,
        };
    }

    private static List<string> IdentityEvidence(ObservedHardwareFacts facts, FormFactorDecision formFactor)
    {
        var evidence = new List<string>
        {
            facts.Identity.Manufacturer is { } manufacturer
                ? $"Manufacturer: {manufacturer}" + (facts.Identity.Model is { } model ? $", model: {model}." : ".")
                : "Manufacturer not reported or a firmware placeholder.",
            $"Form factor: {formFactor.FormFactor}.",
        };
        evidence.AddRange(formFactor.Reasons);
        return evidence;
    }

    private static string DescribePresence(CompanionObservation? observation, CompanionApp app)
    {
        var name = CompanionNames.Of(app);
        return observation switch
        {
            null => $"{name}: not checked.",
            { IsRunning: true } => $"{name}: installed and running.",
            { IsInstalled: true } => $"{name}: installed, not running.",
            _ => $"{name}: not installed.",
        };
    }

    private static CompanionAction? OpenIfInstalled(ObservedHardwareFacts facts, CompanionApp app)
        => facts.IsInstalled(app) is true ? new CompanionAction(CompanionActionKind.Open, app) : null;

    /// <summary>Open when installed, Install when confirmed absent, nothing when never checked.</summary>
    private static CompanionAction? OpenOrInstall(ObservedHardwareFacts facts, CompanionApp app)
        => facts.IsInstalled(app) switch
        {
            true => new CompanionAction(CompanionActionKind.Open, app),
            false => new CompanionAction(CompanionActionKind.Install, app),
            null => null,
        };

    private static string Join(IEnumerable<string> names) => string.Join(" and ", names);
}
