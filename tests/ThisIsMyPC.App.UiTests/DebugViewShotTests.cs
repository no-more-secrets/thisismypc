using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Notifications;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The Debug page: three edge tabs (UI Gallery, Test Controls, State
/// Simulation) in both themes and widths; every test control reaches its
/// service on a click and reports back; the simulation dropdowns change the
/// shared state and Reset clears it. Fakes only: no notification leaves the
/// process and Explorer is never restarted. CI-safe.
/// </summary>
public class DebugViewShotTests
{
    private sealed class RecordingNotifications : INotificationService
    {
        public bool Gate { get; set; } = true;
        public List<AppNotification> Sent { get; } = [];
        public event EventHandler<AppNotification>? NotificationRaised;

        public bool Notify(NotificationType type, string title, string message)
        {
            if (!Gate)
                return false;
            var notification = new AppNotification(type, title, message);
            Sent.Add(notification);
            NotificationRaised?.Invoke(this, notification);
            return true;
        }
    }

    private sealed class RecordingExplorer : IExplorerRestartService
    {
        public int Restarts { get; private set; }
        public Task<OperationResult<bool>> RestartExplorerAsync()
        {
            Restarts++;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public Task<OperationResult<bool>> RefreshExplorerViewsAsync() => Task.FromResult(OperationResult<bool>.Success(true));
    }

    private sealed record Harness(
        DebugViewModel Vm, DebugSimulation Simulation, RecordingNotifications Notifications, RecordingExplorer Explorer,
        List<string> Toasts, List<RestartRequirement> Banners, List<ChangeCategory> Staged);

    private static Harness Create()
    {
        var simulation = new DebugSimulation();
        var notifications = new RecordingNotifications();
        var explorer = new RecordingExplorer();
        var toasts = new List<string>();
        var banners = new List<RestartRequirement>();
        var staged = new List<ChangeCategory>();
        var vm = new DebugViewModel(
            simulation, explorer, notifications,
            showToast: (title, _, severity) => toasts.Add($"{severity}:{title}"),
            showRestartBanner: banners.Add,
            stageSampleChange: staged.Add);
        return new Harness(vm, simulation, notifications, explorer, toasts, banners, staged);
    }

    private static Border Card(Control view)
    {
        var card = new Border
        {
            Name = "TestCard", Margin = new Thickness(16), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Child = view,
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
        return card;
    }

    [AvaloniaFact]
    public void ThreeTabs_RenderInBothThemesAndWidths()
    {
        var harness = Create();
        var card = Card(new DebugView());
        using var session = UiSession.ForView(card, harness.Vm, "debug-page", width: 1200, height: 800);
        Assert.Equal(["UI Gallery", "Test Controls", "State Simulation"],
            session.FindAll<TabItem>(_ => true).Select(t => t.Header as string).ToArray());

        var expected = new Dictionary<string, string[]>
        {
            ["UI Gallery"] = ["Type scale", "Color tokens"],
            ["Test Controls"] = ["Send test notification", "Restart Explorer now", "Show reboot banner", "Stage sample enable"],
            ["State Simulation"] = ["Nothing simulated. The app shows what it detected.", "Windows edition", "Owner Mode service", "Hardware ecosystems", "OpenRGB"],
        };

        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var width in new[] { 1200, 800 })
        {
            session.SetTheme(theme);
            session.Window.Width = width;
            session.Pump();
            foreach (var (header, texts) in expected)
            {
                session.Click(session.Find<TabItem>(t => t.Header as string == header));
                var strip = session.Find<Border>(b => b.Name == "PART_Strip");
                Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
                foreach (var text in texts)
                    Assert.True(session.IsTextVisible(text), $"{header} at {width} should show '{text}': {session.DescribeVisibleText()}");
                session.Screenshot($"{header.Replace(' ', '-').ToLowerInvariant()}-{theme.Key}-{width}");
            }
        }
        session.SetTheme(ThemeVariant.Dark);
    }

    [AvaloniaFact]
    public async Task TestControls_ReachTheirServicesOnClick_AndReportBack()
    {
        var harness = Create();
        using var session = UiSession.ForView(Card(new DebugView()), harness.Vm, "debug-page", width: 1200, height: 800);
        session.ClickText("Test Controls");

        session.ClickText("Send test notification");
        Assert.Single(harness.Notifications.Sent);
        Assert.Equal(NotificationType.Monitoring, harness.Notifications.Sent[0].Type);
        Assert.StartsWith("Raised.", harness.Vm.TestControlsResult, StringComparison.Ordinal);

        harness.Notifications.Gate = false;
        session.ClickText("Send test notification");
        Assert.Single(harness.Notifications.Sent);
        Assert.StartsWith("Blocked by your settings", harness.Vm.TestControlsResult, StringComparison.Ordinal);
        Assert.True(session.IsTextVisible(harness.Vm.TestControlsResult));

        session.ClickText("Show info toast");
        session.ClickText("Show warning toast");
        Assert.Equal(["Info:Test toast", "Warning:Test warning"], harness.Toasts);

        Assert.Equal(0, harness.Explorer.Restarts);
        session.ClickText("Restart Explorer now");
        await session.WaitForAsync(() => !harness.Vm.IsRestartingExplorer, what: "explorer restart");
        Assert.Equal(1, harness.Explorer.Restarts);
        Assert.Equal("Explorer restarted.", harness.Vm.TestControlsResult);

        // Under simulation the restart button refuses and the shell is left alone.
        harness.Simulation.Sku = WindowsSku.Home;
        session.Pump();
        session.ClickText("Restart Explorer now");
        await session.WaitForAsync(() => !harness.Vm.IsRestartingExplorer, what: "refused restart");
        Assert.Equal(1, harness.Explorer.Restarts);
        Assert.Equal(DebugSimulation.BlockedMessage, harness.Vm.TestControlsResult);
        harness.Simulation.Reset();
        session.Pump();

        session.ClickText("Show Explorer restart banner");
        session.ClickText("Show reboot banner");
        session.ClickText("Show sign-out banner");
        Assert.Equal([RestartRequirement.ExplorerRestart, RestartRequirement.Reboot, RestartRequirement.SignOut], harness.Banners);

        session.ClickText("Stage sample enable");
        session.ClickText("Stage sample disable");
        session.ClickText("Stage sample modify");
        Assert.Equal([ChangeCategory.Enable, ChangeCategory.Disable, ChangeCategory.Modify], harness.Staged);
        session.Screenshot("test-controls-after-clicks");
    }

    [AvaloniaFact]
    public void Simulation_DropdownsDriveTheSharedState_AndResetClearsIt()
    {
        var harness = Create();
        using var session = UiSession.ForView(Card(new DebugView()), harness.Vm, "debug-page", width: 1200, height: 800);
        session.ClickText("State Simulation");
        Assert.False(harness.Vm.IsSimulationActive);
        var reset = session.Find<Button>(b => b.Content as string == "Reset");
        Assert.False(reset.IsEffectivelyEnabled);

        harness.Vm.SelectedSku = harness.Vm.SkuOptions.Single(o => o.Value == WindowsSku.Home);
        harness.Vm.SelectedDevice = harness.Vm.DeviceOptions.Single(o => o.DisplayName == "ASUS laptop");
        harness.Vm.SelectedOwnerMode = harness.Vm.OwnerModeOptions.Single(o => o.Value == OwnerModeState.NotInstalled);
        var openRgb = harness.Vm.Capabilities.Single(c => c.Capability == SystemCapability.OpenRgb);
        openRgb.Selected = openRgb.Options.Single(o => o.Value == true);
        session.Pump();

        Assert.True(harness.Simulation.IsActive);
        Assert.Equal(WindowsSku.Home, harness.Simulation.Sku);
        Assert.Equal("ASUSTeK COMPUTER INC.", harness.Simulation.Device!.Manufacturer);
        Assert.Equal(OwnerModeState.NotInstalled, harness.Simulation.OwnerModeState);
        Assert.True(harness.Simulation.GetCapability(SystemCapability.OpenRgb));
        Assert.True(harness.Vm.IsSimulationActive);
        Assert.Equal("Simulated: Windows 11 Home, ASUS laptop, Owner Mode service not installed, OpenRGB present.", harness.Vm.SimulationSummary);
        Assert.True(session.IsTextVisible(harness.Vm.SimulationSummary));
        Assert.True(reset.IsEffectivelyEnabled);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot($"simulation-active-{theme.Key}");
        }
        session.SetTheme(ThemeVariant.Dark);

        // A change made elsewhere (the window banner's Reset) shows here too.
        harness.Simulation.Reset();
        session.Pump();
        Assert.False(harness.Vm.IsSimulationActive);
        Assert.Null(harness.Vm.SelectedSku.Value);
        Assert.Null(harness.Vm.SelectedDevice.Preset);
        Assert.Null(harness.Vm.SelectedOwnerMode.Value);
        Assert.Null(openRgb.Selected.Value);
        Assert.False(reset.IsEffectivelyEnabled);
        Assert.True(session.IsTextVisible("Nothing simulated. The app shows what it detected."));

        // And the page's own Reset button clears state set from outside.
        harness.Simulation.Sku = WindowsSku.Pro;
        session.Pump();
        Assert.Equal(WindowsSku.Pro, harness.Vm.SelectedSku.Value);
        session.Click(reset);
        Assert.False(harness.Simulation.IsActive);
    }
}
