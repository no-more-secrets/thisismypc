using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Settings split into Application, Notifications, Owner Mode, Modules, and
/// Backup &amp; Transfer tabs. Temp settings file and fake contributors, so
/// CI-safe; the Owner Mode service card needs the real SCM and is absent here.
/// </summary>
public class SettingsTabsShotTests
{
    private static readonly string[] TabHeaders =
        ["Application", "Notifications", "Owner Mode", "Modules", "Backup & Transfer"];

    private sealed class FakeContributor : IModuleSettingsContributor
    {
        public string ModuleId => "Fake Module";
        public IReadOnlyList<ModuleSettingDefinition> SettingDefinitions { get; } =
        [
            new("show-extras", "Show extras", "Adds the extra rows.", ModuleSettingType.Toggle, "0"),
        ];
    }

    /// <summary>A service lifecycle that never reaches the SCM: state is whatever the test says.</summary>
    private sealed class FakeServiceControl : ThisIsMyPC.App.Services.IOwnerModeServiceControl
    {
        public ThisIsMyPC.App.Services.OwnerModeState State { get; set; } = ThisIsMyPC.App.Services.OwnerModeState.Stopped;
        public TaskCompletionSource<Core.Results.OperationResult<bool>>? Pending { get; set; }
        public event EventHandler? StateChanged;

        public ThisIsMyPC.App.Services.OwnerModeState GetState() => State;

        public async Task<Core.Results.OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default)
        {
            var result = Pending is { } pending ? await pending.Task : Core.Results.OperationResult<bool>.Success(true);
            if (result.IsSuccess)
            {
                State = ThisIsMyPC.App.Services.OwnerModeState.Running;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            return result;
        }

        public Task<Core.Results.OperationResult<bool>> DisableAsync(CancellationToken cancellationToken = default)
        {
            State = ThisIsMyPC.App.Services.OwnerModeState.Disabled;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(Core.Results.OperationResult<bool>.Success(true));
        }
    }

    private static SettingsViewModel Model(bool withModules, OwnerModeSectionViewModel? ownerMode = null) => new(
        new SettingsService(Path.Combine(Path.GetTempPath(), $"tipc-settings-tabs-{Guid.NewGuid():N}.json")),
        withModules ? [new FakeContributor()] : [],
        capabilityReport:
        [
            new CapabilityReportRow(SystemCapability.DdcCi, "DDC/CI monitors", new ModuleAvailability(true, RemediationHint: "")),
            new CapabilityReportRow(SystemCapability.OpenRgb, "OpenRGB", new ModuleAvailability(false, "OpenRGB is not running.", "Start OpenRGB with its SDK server on.")),
        ],
        ownerMode: ownerMode);

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
    public void FiveTabs_EachShowingItsOwnRows_InBothThemesAndWidths()
    {
        var vm = Model(withModules: true);
        var card = Card(new SettingsView());
        using var session = UiSession.ForView(card, vm, "settings-tabs", width: 1200, height: 800);
        Assert.Equal(TabHeaders, session.FindAll<TabItem>(_ => true).Select(t => t.Header as string).ToArray());

        var expectedText = new Dictionary<string, string[]>
        {
            ["Application"] = ["Theme", "Dyslexia-friendly font", "Tray mode", "Start with Windows", "Check for app updates", "Automatically download updates"],
            ["Notifications"] = ["Notifications", "Notify: monitoring alerts", "Notify: update available"],
            ["Owner Mode"] = ["In-app monitoring", "Startup & service monitoring", "Runs inside the app while it is open. Does not need the Owner Mode service."],
            ["Modules"] = ["Fake Module", "Show extras", "System Capabilities", "DDC/CI monitors", "OpenRGB"],
            ["Backup & Transfer"] = ["Export Settings...", "Import Settings..."],
        };
        var notExpected = new Dictionary<string, string[]>
        {
            // "Notifications" and "Owner Mode" are also tab headers, so the checks use row names.
            ["Application"] = ["Notify: monitoring alerts", "Startup & service monitoring"],
            ["Notifications"] = ["Tray mode", "Check for app updates", "Startup & service monitoring"],
            ["Owner Mode"] = ["Notify: monitoring alerts"],
        };

        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            foreach (var width in new[] { 1200, 800 })
            {
                session.Window.Width = width;
                session.Pump();
                for (var i = 0; i < TabHeaders.Length; i++)
                {
                    session.Click(session.Find<TabItem>(t => t.Header as string == TabHeaders[i]));
                    Assert.Equal(i, vm.SelectedTabIndex);
                    var strip = session.Find<Border>(b => b.Name == "PART_Strip");
                    Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
                    Assert.Equal(session.TopOf(card) + 1, session.TopOf(strip), 0.5);
                    foreach (var text in expectedText[TabHeaders[i]])
                        Assert.True(session.IsTextVisible(text), $"{TabHeaders[i]} should show '{text}'");
                    if (notExpected.TryGetValue(TabHeaders[i], out var absent))
                    {
                        foreach (var text in absent)
                            Assert.False(session.IsTextVisible(text), $"{TabHeaders[i]} should not show '{text}'");
                    }
                    if (theme == ThemeVariant.Dark || width == 1200)
                        session.Screenshot($"{TabHeaders[i].Replace(" & ", "-", StringComparison.Ordinal).ToLowerInvariant()}-{theme.Key}-{width}");
                }
            }
        }
    }

    [AvaloniaFact]
    public async Task OwnerModeTab_RendersServiceStates_WithAFakeService()
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var service = new FakeServiceControl { State = ThisIsMyPC.App.Services.OwnerModeState.Running };
            var section = new OwnerModeSectionViewModel(service);
            var vm = Model(withModules: false, section);
            using var session = UiSession.ForView(Card(new SettingsView()), vm, "settings-tabs", width: 1200, height: 800);
            session.SetTheme(theme);
            session.ClickText("Owner Mode");
            Assert.True(session.IsTextVisible("Owner Mode Service"));
            Assert.True(session.IsTextVisible("Running"));
            Assert.True(session.IsTextVisible("Startup & service monitoring"));
            Assert.False(session.IsTextVisible("The Owner Mode service is not available in this build."));
            session.Screenshot($"owner-mode-running-{theme.Key}");

            // Disable through the button: the fake flips to Disabled.
            session.ClickText("Disable Service");
            await session.WaitForAsync(() => !section.IsBusy && !section.IsRunning, what: "service disabled");
            Assert.True(session.IsTextVisible("Installed, disabled"));
            session.Screenshot($"owner-mode-disabled-{theme.Key}");

            // Enable that hangs: the busy bar shows while the fake waits.
            service.Pending = new TaskCompletionSource<Core.Results.OperationResult<bool>>();
            session.ClickText("Enable Service");
            await session.WaitForAsync(() => section.IsBusy, what: "busy state");
            Assert.True(session.Find<ProgressBar>(_ => true).IsEffectivelyVisible);
            session.Screenshot($"owner-mode-busy-{theme.Key}");

            // The enable fails: the error sits inline and the state stays real.
            service.Pending.SetResult(Core.Results.OperationResult<bool>.Failure(
                "Service binary not found at C:\\Program Files\\ThisIsMyPC\\ThisIsMyPC.Service.exe. Reinstall ThisIsMyPC to restore it.",
                Core.Results.ErrorCategory.NotFound));
            await session.WaitForAsync(() => !section.IsBusy, what: "enable finished");
            Assert.False(section.IsRunning);
            Assert.StartsWith("Service binary not found", section.ErrorText, StringComparison.Ordinal);
            Assert.True(session.IsTextVisible(section.ErrorText));
            session.Screenshot($"owner-mode-error-{theme.Key}");

            // A second enable succeeds and clears the error.
            service.Pending = null;
            session.ClickText("Enable Service");
            await session.WaitForAsync(() => section.IsRunning, what: "service running");
            Assert.Equal("", section.ErrorText);
            Assert.True(session.IsTextVisible("Running"));
        }
    }

    [AvaloniaFact]
    public void ModulesTab_StaysReachable_WithoutModulePreferences()
    {
        var vm = Model(withModules: false);
        using var session = UiSession.ForView(Card(new SettingsView()), vm, "settings-tabs", width: 1200, height: 800);
        Assert.False(vm.HasModuleSections);
        session.ClickText("Modules");
        Assert.Equal(3, vm.SelectedTabIndex);
        Assert.True(session.IsTextVisible("No installed module has preferences yet."));
        Assert.True(session.IsTextVisible("System Capabilities"));
        Assert.True(session.IsTextVisible("OpenRGB"));
        session.Screenshot("modules-no-preferences");

        session.ClickText("Owner Mode");
        Assert.True(session.IsTextVisible("The Owner Mode service is not available in this build."));
        Assert.True(session.IsTextVisible("Startup & service monitoring"));
        session.Screenshot("owner-mode-no-service");
    }
}
