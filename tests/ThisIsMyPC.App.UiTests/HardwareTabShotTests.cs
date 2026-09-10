using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Hardware.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// CI-safe: renders the shared Hardware tab page for every availability the
/// policy can produce, from fake facts, in both themes. Install runs against
/// the real in-memory pending-actions queue (never applied); Open runs
/// against a fake user context, so nothing is installed or launched.
/// </summary>
public class HardwareTabShotTests
{
    private sealed class FakeUser : IInteractiveUserContext
    {
        public List<string> Launched { get; } = [];
        public bool IsCallerElevated => false;
        public InteractiveUser? Current => new() { Sid = "S-1-5-21-1", AccountName = @"PC\tester", SessionId = 1 };

        public OperationResult<bool> LaunchAsUser(string applicationPath, string? arguments = null)
        {
            Launched.Add(applicationPath);
            return OperationResult<bool>.Success(true);
        }

        public OperationResult<T> RunAsUser<T>(Func<T> action) => OperationResult<T>.Success(action());
    }

    private static readonly FormFactorEvidence Desktop = new()
    {
        SmbiosChassisTypes = [3],
        PlatformRole = PlatformRole.Desktop,
        HasSystemBattery = false,
    };

    private static ObservedHardwareFacts Facts(FormFactorEvidence formFactor, params CompanionObservation[] companions)
    {
        var list = new List<CompanionObservation>(companions);
        foreach (var app in Enum.GetValues<CompanionApp>())
        {
            if (list.All(o => o.App != app))
                list.Add(CompanionObservation.NotInstalled(app));
        }
        return new ObservedHardwareFacts
        {
            Identity = MachineIdentity.From("ASUSTeK COMPUTER INC.", "ROG STRIX X670E-E GAMING WIFI"),
            FormFactor = formFactor,
            AsusPlatformDriverPresent = false,
            Companions = list,
        };
    }

    private static HardwareTabScanData Data(ObservedHardwareFacts facts, HardwareDomain domain, string? launchPath = null, bool showAll = false)
    {
        var report = HardwareCompatibilityPolicy.Decide(facts, new HardwareCompatibilityOptions(showAll));
        return new HardwareTabScanData(
            report.For(domain), report, launchPath,
            ["FanControl: scheduled task '\\FanControl' (enabled).", "OpenRGB: uninstall entry 'OpenRGB'.", "G-Helper: not found."],
            new DateTimeOffset(2026, 9, 9, 14, 30, 0, TimeSpan.Zero));
    }

    [AvaloniaFact]
    public void Cooling_WithFanControlRunning_OffersOpen_AndOpenLaunchesAsUser()
    {
        var user = new FakeUser();
        var actions = new HardwareCompanionActions(new PendingActionsService(), user);
        var data = Data(Facts(Desktop, CompanionObservation.Running(CompanionApp.FanControl, HardwareDomain.Cooling)),
            HardwareDomain.Cooling, launchPath: Environment.ProcessPath);
        using var vm = new HardwareTabViewModel(data, actions);

        using var session = UiSession.ForView(new HardwareTabView(), vm, "hardware-tab", width: 976, height: 676);
        session.Screenshot("cooling-available-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("cooling-available-light");

        Assert.True(session.IsTextVisible("Available"));
        Assert.True(session.IsTextVisible("Open FanControl"));
        Assert.True(session.IsTextVisible("ASUSTeK COMPUTER INC. ROG STRIX X670E-E GAMING WIFI, desktop"));

        session.ClickText("Open FanControl");
        session.Pump();
        Assert.Equal([Environment.ProcessPath], user.Launched);
        Assert.True(session.IsTextVisible("FanControl is opening."));
    }

    [AvaloniaFact]
    public void Cooling_WithoutFanControl_OffersInstall_AndInstallQueuesTheAction()
    {
        var queue = new PendingActionsService();
        var actions = new HardwareCompanionActions(queue, new FakeUser());
        using var vm = new HardwareTabViewModel(Data(Facts(Desktop), HardwareDomain.Cooling), actions);

        using var session = UiSession.ForView(new HardwareTabView(), vm, "hardware-tab", width: 976, height: 676);
        session.Screenshot("cooling-install-dark");

        Assert.True(session.IsTextVisible("Not available"));
        Assert.True(session.IsTextVisible("Install FanControl"));

        session.ClickText("Install FanControl");
        session.Pump();
        Assert.Single(queue.PendingActions);
        Assert.Equal("install:fancontrol", queue.PendingActions[0].ActionId);
        Assert.True(session.IsTextVisible("FanControl queued for install"));
        Assert.True(session.IsTextVisible("FanControl is queued. Apply the queued changes to install it."));
        Assert.False(session.IsTextVisible("Apply the queued changes to run the install."));
        session.Screenshot("cooling-install-queued-dark");

        // Clicking again is a no-op: the button is disabled once queued.
        session.ClickText("FanControl queued for install");
        session.Pump();
        Assert.Single(queue.PendingActions);

        // Discarding from the review panel re-enables the button on this page.
        queue.DiscardAll();
        session.Pump();
        Assert.True(session.IsTextVisible("Install FanControl"));
        Assert.True(session.Find<Button>(b => b.Content is "Install FanControl").IsEffectivelyEnabled);

        // A fresh page open with the action queued explains the disabled button instead.
        queue.Stage(Modules.Software.Actions.SoftwareActionFactory.CreateInstall(
            Modules.Software.Services.SoftwareCatalog.Entries.Single(e => e.Id == "fancontrol")));
        using var reopened = new HardwareTabViewModel(Data(Facts(Desktop), HardwareDomain.Cooling), actions);
        Assert.Equal("Apply the queued changes to run the install.", reopened.ActionHint);
        Assert.False(reopened.CanRunAction);
    }

    [AvaloniaFact]
    public void Install_WhenTheSoftwareModuleIsUnavailable_IsDisabledWithAHint()
    {
        var actions = new HardwareCompanionActions(new PendingActionsService(), new FakeUser());
        using var vm = new HardwareTabViewModel(Data(Facts(Desktop), HardwareDomain.Cooling), actions, installAvailable: false);

        using var session = UiSession.ForView(new HardwareTabView(), vm, "hardware-tab", width: 976, height: 676);
        session.Screenshot("cooling-install-no-winget-dark");

        Assert.False(session.Find<Button>(b => b.Content is "Install FanControl").IsEffectivelyEnabled);
        Assert.True(session.IsTextVisible("Installs need the app installer (winget), which the Software page reports as unavailable on this PC."));
    }

    [AvaloniaFact]
    public void Lighting_DevicesOwnedByArmouryCrate_IsAConflict_WithNoButton()
    {
        var facts = Facts(Desktop,
            CompanionObservation.Running(CompanionApp.OpenRgb),
            CompanionObservation.Running(CompanionApp.ArmouryCrate, HardwareDomain.Lighting)) with
        {
            LightingDevices = [new LightingDeviceSummary("ASUS ROG STRIX GeForce RTX 4080 Gaming", LightingDeviceType.Gpu, "ENE SMBus", "I2C: NVIDIA NvAPI I2C on GPU 0, address 0x67")],
        };
        using var vm = new HardwareTabViewModel(Data(facts, HardwareDomain.Lighting, launchPath: Environment.ProcessPath));

        using var session = UiSession.ForView(new HardwareTabView(), vm, "hardware-tab", width: 976, height: 676);
        session.Screenshot("lighting-conflict-dark");

        Assert.True(session.IsTextVisible("In use elsewhere"));
        Assert.True(session.IsTextVisible("Armoury Crate currently controls these devices. Close it, or manage them there, before using this tab."));
        Assert.Null(session.TryFind<Button>(b => b.Content is string s && s.StartsWith("Open", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public void SystemControl_OnADesktop_ExplainsWhy_AndDetailsExpand()
    {
        using var vm = new HardwareTabViewModel(Data(Facts(Desktop), HardwareDomain.SystemControl));

        using var session = UiSession.ForView(new HardwareTabView(), vm, "hardware-tab", width: 976, height: 676);
        Assert.True(session.IsTextVisible("Not available"));
        Assert.True(session.IsTextVisible("System Control covers laptop performance modes, battery limits and keyboard controls. This PC is a desktop."));
        Assert.False(session.IsTextVisible("Form factor: Desktop."));

        session.ClickText("Details");
        session.Pump();
        session.Screenshot("system-control-details-dark");
        Assert.True(session.IsTextVisible("Form factor: Desktop."));
        Assert.True(session.IsTextVisible("G-Helper: not found."));
    }

    [AvaloniaFact]
    public void Monitoring_NotIntegrated_IsPending_AndOverrideShowsControlsWithTheWarning()
    {
        using var plain = new HardwareTabViewModel(Data(Facts(Desktop), HardwareDomain.Monitoring));
        using (var session = UiSession.ForView(new HardwareTabView(), plain, "hardware-tab", width: 976, height: 676))
        {
            session.Screenshot("monitoring-pending-dark");
            Assert.True(session.IsTextVisible("Not verified"));
            Assert.False(session.IsTextVisible("Shown by the Advanced setting. Nothing here can write to hardware on this PC."));
        }

        using var overridden = new HardwareTabViewModel(Data(Facts(Desktop), HardwareDomain.Monitoring, showAll: true));
        using (var session = UiSession.ForView(new HardwareTabView(), overridden, "hardware-tab", width: 976, height: 676))
        {
            session.Screenshot("monitoring-override-dark");
            Assert.True(session.IsTextVisible("Shown by the Advanced setting. Nothing here can write to hardware on this PC."));
            Assert.True(session.IsTextVisible("Sensor readings arrive with the LibreHardwareMonitor integration. Nothing is read yet."));
            Assert.False(overridden.Decision.LiveWritesAllowed);
        }
    }

    [AvaloniaFact]
    public void Open_WithoutALaunchPath_IsDisabledWithAHint()
    {
        var actions = new HardwareCompanionActions(new PendingActionsService(), new FakeUser());
        var data = Data(Facts(Desktop, CompanionObservation.Installed(CompanionApp.FanControl)), HardwareDomain.Cooling, launchPath: null);
        using var vm = new HardwareTabViewModel(data, actions);

        using var session = UiSession.ForView(new HardwareTabView(), vm, "hardware-tab", width: 976, height: 676);
        session.Screenshot("cooling-no-path-dark");

        var button = session.Find<Button>(b => b.Content is "Open FanControl");
        Assert.False(button.IsEffectivelyEnabled);
        Assert.True(session.IsTextVisible("Its location could not be found. Open it from the Start menu."));
    }

    [AvaloniaFact]
    public void Apply_ReplacesEveryDisplayedValue()
    {
        using var vm = new HardwareTabViewModel(Data(Facts(Desktop), HardwareDomain.Cooling));
        using var session = UiSession.ForView(new HardwareTabView(), vm, "hardware-tab", width: 976, height: 676);
        Assert.True(session.IsTextVisible("Install FanControl"));

        vm.Apply(Data(Facts(Desktop, CompanionObservation.Running(CompanionApp.FanControl)), HardwareDomain.Cooling, launchPath: Environment.ProcessPath));
        session.Pump();

        Assert.True(session.IsTextVisible("Available"));
        Assert.True(session.IsTextVisible("Open FanControl"));
        Assert.False(session.IsTextVisible("Install FanControl"));
    }
}
