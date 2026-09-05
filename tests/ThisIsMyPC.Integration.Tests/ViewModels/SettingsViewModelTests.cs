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
    public void Sections_GeneralAndPersistence_AlwaysPresent()
    {
        var vm = new SettingsViewModel(_settings, []);

        Assert.Equal(["General", "Persistence & Background"], vm.Sections.Select(s => s.Header));
        Assert.False(vm.HasModuleSections);
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
