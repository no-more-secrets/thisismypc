using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.Controls;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// UI/UX chapter closers: the in-app toast stack and the actionable Owner Mode
/// callout on setting cards. Fake data only; CI-safe.
/// </summary>
public class ToastAndOwnerModeShotTests
{
    // ---- Toast stack ----

    [AvaloniaFact]
    public void ToastStack_RendersAllSeverities()
    {
        var stack = new ToastStackViewModel(lifetime: TimeSpan.Zero);
        stack.Show("Update available", "ThisIsMyPC 1.2.0 is available - click the badge to open the releases page", ToastSeverity.Info);
        stack.Show("Changes applied", "3 changes applied successfully", ToastSeverity.Success);
        stack.Show("New startup entry", "EvilUpdater.exe registered itself to run at boot", ToastSeverity.Warning);

        using var session = UiSession.ForView(new ToastHostControl(), stack, "toast-host", width: 420, height: 400);
        session.Screenshot("all-severities");

        Assert.True(session.IsTextVisible("Update available"));
        Assert.True(session.IsTextVisible("Changes applied"));
        Assert.True(session.IsTextVisible("New startup entry"));
    }

    [AvaloniaFact]
    public void ToastStack_LightTheme()
    {
        var stack = new ToastStackViewModel(lifetime: TimeSpan.Zero);
        stack.Show("Update available", "ThisIsMyPC 1.2.0 is available", ToastSeverity.Info);
        stack.Show("New startup entry", "EvilUpdater.exe registered itself to run at boot", ToastSeverity.Warning);

        using var session = UiSession.ForView(new ToastHostControl(), stack, "toast-host", width: 420, height: 300);
        session.SetTheme(Avalonia.Styling.ThemeVariant.Light);
        session.Screenshot("light-theme");
    }

    [AvaloniaFact]
    public void Toast_CloseButton_DismissesTheCard()
    {
        var stack = new ToastStackViewModel(lifetime: TimeSpan.Zero);
        stack.Show("Update available", "ThisIsMyPC 1.2.0 is available", ToastSeverity.Info);

        using var session = UiSession.ForView(new ToastHostControl(), stack, "toast-host", width: 420, height: 300);
        var close = session.Find<Avalonia.Controls.Button>(_ => true);
        session.Click(close);

        Assert.Empty(stack.Toasts);
        Assert.False(session.IsTextVisible("Update available"));
    }

    // ---- Actionable Owner Mode callout ----

    private sealed class ShotDetector : ICapabilityDetector
    {
        public bool OwnerMode { get; set; }
        public WindowsSku? Sku => null;
        public string? SkuDetectionFailureReason => null;
        public bool IsSkuRestricted(WindowsSku? restriction) => false;
        public bool IsAvailable(SystemCapability capability) => true;
        public ModuleAvailability GetAvailability(SystemCapability capability) => new(true);
        public bool IsOwnerModeAvailable => OwnerMode;
        public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() => [];
    }

    private sealed class ShotLifecycle(ShotDetector detector) : IOwnerModeLifecycle
    {
        public event EventHandler? StateChanged;

        public Task<OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default)
        {
            detector.OwnerMode = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
    }

    private static SettingCardViewModel CreateDegradedCard(ShotDetector detector, IOwnerModeLifecycle? lifecycle)
    {
        var source = new SettingCardSource
        {
            Model = new SettingCardModel
            {
                SettingId = "owner-mode-shot",
                ModuleId = "Test",
                DisplayName = "Keep telemetry disabled",
                Description = "A setting whose enforcement needs the background service.",
                ControlType = SettingControlType.Toggle,
                CurrentValue = "0",
                RegistryPath = @"HKLM\SOFTWARE\Example",
                ValueName = "Example",
                OwnerModeRequired = true,
            },
            CreateToggleGroup = _ => new ChangeGroup
            {
                GroupId = Guid.NewGuid().ToString("N"),
                DisplayName = "Keep telemetry disabled",
                Description = "shot",
                Changes = [],
            },
            ReadCurrentState = () => false,
        };
        return new SettingCardViewModel(source, new PendingChangesService(), detector, lifecycle);
    }

    [AvaloniaFact]
    public void DegradedCard_ShowsCalloutAndTurnOnButton()
    {
        var detector = new ShotDetector();
        var vm = CreateDegradedCard(detector, new ShotLifecycle(detector));

        using var session = UiSession.ForView(new SettingCardControl(), vm, "owner-mode-card", width: 700, height: 260);
        session.Screenshot("degraded-with-action");

        Assert.True(session.IsTextVisible("Turn on Owner Mode"));
    }

    [AvaloniaFact]
    public async Task ClickingTurnOn_UnDegradesTheCardToTheBadge()
    {
        var detector = new ShotDetector();
        var vm = CreateDegradedCard(detector, new ShotLifecycle(detector));

        using var session = UiSession.ForView(new SettingCardControl(), vm, "owner-mode-card", width: 700, height: 260);
        session.ClickText("Turn on Owner Mode");
        await session.WaitForAsync(() => !vm.IsOwnerModeDegraded, timeoutMs: 5000, what: "card un-degrading");
        session.Screenshot("enabled-with-badge");

        // IsTextVisible can't see ancestor-hidden state, so ask the VM; the
        // screenshot above is the pixel proof.
        Assert.False(vm.CanTurnOnOwnerMode);
        Assert.True(session.IsTextVisible("Owner Mode"));
        Assert.True(vm.IsControlEnabled);
    }

    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public void MainWindow_ToastOverlay_SitsTopRightOverContent()
    {
        using var session = UiSession.ForMainWindow("toast-mainwindow");
        var vm = (MainWindowViewModel)session.Window.DataContext!;

        vm.ToastStack.Show("Update available", "ThisIsMyPC 1.2.0 is available - click the badge to open the releases page", ToastSeverity.Info);
        vm.ToastStack.Show("New startup entry", "EvilUpdater.exe registered itself to run at boot", ToastSeverity.Warning);
        session.Screenshot("toasts-over-home");

        Assert.True(session.IsTextVisible("Update available"));
        Assert.True(session.IsTextVisible("New startup entry"));
    }

    [AvaloniaFact]
    public void DegradedCard_WithoutLifecycle_CalloutOnlyNoButton()
    {
        var vm = CreateDegradedCard(new ShotDetector(), lifecycle: null);

        using var session = UiSession.ForView(new SettingCardControl(), vm, "owner-mode-card", width: 700, height: 260);
        session.Screenshot("degraded-no-action");

        Assert.False(session.IsTextVisible("Turn on Owner Mode"));
        Assert.True(session.IsTextVisible("Needs Owner Mode. The background service keeps this setting applied when Windows reverts it."));
    }
}
