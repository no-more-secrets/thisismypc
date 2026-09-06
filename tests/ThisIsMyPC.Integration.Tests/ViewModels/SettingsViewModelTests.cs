using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-svm-{Guid.NewGuid():N}");
    private readonly SettingsService _settings;

    public SettingsViewModelTests()
    {
        _settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        _settings.Initialize();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    private sealed class FakeContributor : IModuleSettingsContributor
    {
        public string ModuleId => "Fake Module";
        public IReadOnlyList<ModuleSettingDefinition> SettingDefinitions { get; } =
        [
            new("show-extras", "Show extras", "d", ModuleSettingType.Toggle, "0"),
            new("scan-depth", "Scan depth", "d", ModuleSettingType.Choice, "shallow",
                [("shallow", "Shallow"), ("deep", "Deep")]),
        ];
    }

    [Fact]
    public void LogonChoicesPersistAndAutomaticDownloadsFollowUpdateChecking()
    {
        _settings.SetApp(AppSettingKeys.AutoStart, "1");
        var vm = new SettingsViewModel(_settings, []);
        var logon = vm.ApplicationSection.Items.OfType<SettingChoiceItemViewModel>().Single(t => t.DisplayName == "Start with Windows");
        Assert.Equal(new[] { "Disabled", "Minimized", "Open at logon" }, logon.Options.Select(o => o.DisplayName));
        Assert.Equal("Minimized", logon.Selected!.DisplayName);
        logon.Selected = logon.Options[2];
        Assert.Equal("2", _settings.GetApp(AppSettingKeys.AutoStart, "0"));
        var toggles = vm.ApplicationSection.Items.OfType<SettingToggleItemViewModel>().ToList();
        var check = toggles.Single(t => t.DisplayName == "Check for app updates");
        var download = toggles.Single(t => t.DisplayName == "Automatically download updates");
        Assert.False(download.IsOn);
        Assert.True(download.IsEnabled);
        download.IsOn = true;
        check.IsOn = false;
        Assert.False(download.IsEnabled);
        Assert.True(download.IsOn);
        check.IsOn = true;
        Assert.True(download.IsEnabled);
    }

    [Fact]
    public void Sections_ApplicationNotificationsMonitoring_AlwaysPresent()
    {
        var vm = new SettingsViewModel(_settings, []);

        Assert.Equal(
            [SettingsViewModel.ApplicationHeader, SettingsViewModel.NotificationsHeader, SettingsViewModel.MonitoringHeader],
            vm.Sections.Select(s => s.Header));
        Assert.False(vm.HasModuleSections);
        Assert.Empty(vm.ModuleSections);
    }

    [Fact]
    public void EveryAppKey_LivesInItsOwnTab_AndKeepsItsDefault()
    {
        var vm = new SettingsViewModel(_settings, []);
        static string[] Names(SettingsSectionViewModel section) =>
            section.Items.Select(i => i switch
            {
                SettingToggleItemViewModel t => t.DisplayName,
                SettingChoiceItemViewModel c => c.DisplayName,
                SettingTextItemViewModel x => x.DisplayName,
                _ => "?",
            }).ToArray();

        Assert.Equal(["Theme", "Dyslexia-friendly font", "Tray mode", "Start with Windows", "Check for app updates", "Automatically download updates"],
            Names(vm.ApplicationSection));
        Assert.Equal(["Notifications", "Notify: monitoring alerts", "Notify: update available"],
            Names(vm.NotificationsSection));
        Assert.Equal(["Startup & service monitoring"], Names(vm.MonitoringSection));

        var toggles = vm.Sections.SelectMany(s => s.Items).OfType<SettingToggleItemViewModel>().ToList();
        Assert.False(toggles.Single(t => t.DisplayName == "Tray mode").IsOn);
        Assert.Equal("0", vm.ApplicationSection.Items.OfType<SettingChoiceItemViewModel>().Single(t => t.DisplayName == "Start with Windows").Selected!.Value);
        Assert.True(toggles.Single(t => t.DisplayName == "Check for app updates").IsOn);
        Assert.True(toggles.Single(t => t.DisplayName == "Notifications").IsOn);
        Assert.False(toggles.Single(t => t.DisplayName == "Startup & service monitoring").IsOn);

        toggles.Single(t => t.DisplayName == "Startup & service monitoring").IsOn = true;
        toggles.Single(t => t.DisplayName == "Check for app updates").IsOn = false;
        Assert.True(_settings.GetAppBool(AppSettingKeys.MonitoringEnabled, fallback: false));
        Assert.False(_settings.GetAppBool(AppSettingKeys.UpdateCheck, fallback: true));
    }

    [Fact]
    public void ToggleChange_PersistsImmediately()
    {
        var vm = new SettingsViewModel(_settings, []);
        var tray = vm.Sections.SelectMany(s => s.Items)
            .OfType<SettingToggleItemViewModel>()
            .Single(t => t.DisplayName == "Tray mode");

        tray.IsOn = true;

        Assert.True(_settings.GetAppBool(AppSettingKeys.TrayMode, fallback: false));
        Assert.Equal("tray", _settings.GetApp(AppSettingKeys.CloseAction, ""));
        Assert.Equal("taskbar", _settings.GetApp(AppSettingKeys.MinimizeAction, ""));

        tray.IsOn = false;

        Assert.Equal("exit", _settings.GetApp(AppSettingKeys.CloseAction, ""));
    }

    [Fact]
    public void ChoiceChange_PersistsImmediately_AndInvokesApplyCallback()
    {
        string? applied = null;
        var vm = new SettingsViewModel(_settings, [], applyTheme: v => applied = v);
        var theme = vm.Sections.SelectMany(s => s.Items)
            .OfType<SettingChoiceItemViewModel>()
            .Single(c => c.DisplayName == "Theme");

        theme.Selected = theme.Options.Single(o => o.Value == "light");

        Assert.Equal("light", _settings.GetApp(AppSettingKeys.Theme, "?"));
        Assert.Equal("light", applied);
    }

    [Fact]
    public void ModuleContributor_GetsASection_ValuesRoundTripInModuleScope()
    {
        var vm = new SettingsViewModel(_settings, [new FakeContributor()]);

        Assert.True(vm.HasModuleSections);
        var section = vm.Sections.Single(s => s.Header == "Fake Module");
        var toggle = section.Items.OfType<SettingToggleItemViewModel>().Single();
        var choice = section.Items.OfType<SettingChoiceItemViewModel>().Single();

        Assert.False(toggle.IsOn); // default "0"
        Assert.Equal("shallow", choice.Selected!.Value);

        toggle.IsOn = true;
        choice.Selected = choice.Options.Single(o => o.Value == "deep");

        Assert.Equal("1", _settings.GetModule("Fake Module", "show-extras"));
        Assert.Equal("deep", _settings.GetModule("Fake Module", "scan-depth"));

        // A fresh VM reads the stored values back
        var second = new SettingsViewModel(_settings, [new FakeContributor()]);
        var secondToggle = second.Sections.Single(s => s.Header == "Fake Module")
            .Items.OfType<SettingToggleItemViewModel>().Single();
        Assert.True(secondToggle.IsOn);
    }

    [Fact]
    public void LegacyWindowActions_AreNotShown()
    {
        _settings.SetApp(AppSettingKeys.CloseAction, "tray");
        var vm = new SettingsViewModel(_settings, []);

        Assert.DoesNotContain(vm.Sections.SelectMany(s => s.Items)
            .OfType<SettingChoiceItemViewModel>(), c => c.DisplayName.StartsWith("When I", StringComparison.Ordinal));
    }
}
