using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Settings;
using ThisIsMyPC.Integration.Tests.Fakes;

namespace ThisIsMyPC.Integration.Tests.Services;

public sealed class AutoStartServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-autostart-{Guid.NewGuid():N}");
    private readonly SettingsService _settings;
    private readonly StoringFakeRegistryService _registry = new();

    public AutoStartServiceTests()
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

    private AutoStartService Create() => new(_registry, _settings, @"C:\Apps\ThisIsMyPC.exe");

    private string? RunValue() =>
        _registry.ReadString(AutoStartService.RunKeyPath, AutoStartService.RunValueName) is { IsSuccess: true } r
            ? r.Value : null;

    [Theory]
    [InlineData("1", " --minimized")]
    [InlineData("2", "")]
    public void LogonModeWritesExpectedLaunchArguments(string mode, string suffix)
    {
        using var service = Create();
        _settings.SetApp(AppSettingKeys.AutoStart, mode);
        Assert.Equal("\"C:\\Apps\\ThisIsMyPC.exe\"" + suffix, RunValue());
        service.Reconcile();
        Assert.Equal("\"C:\\Apps\\ThisIsMyPC.exe\"" + suffix, RunValue());
        _settings.SetApp(AppSettingKeys.AutoStart, "0");
        Assert.Null(RunValue());
    }

    [Fact]
    public void Default_ReconcileCreatesNoEntry()
    {
        using var service = Create();
        service.Reconcile();

        Assert.Null(RunValue());
    }

    [Fact]
    public void EnablingTheSetting_WritesQuotedCommandWithMinimizedFlag()
    {
        using var service = Create();

        _settings.SetApp(AppSettingKeys.AutoStart, "1");

        Assert.Equal("\"C:\\Apps\\ThisIsMyPC.exe\" --minimized", RunValue());
    }

    [Fact]
    public void DisablingTheSetting_RemovesTheEntry()
    {
        using var service = Create();
        _settings.SetApp(AppSettingKeys.AutoStart, "1");
        Assert.NotNull(RunValue());

        _settings.SetApp(AppSettingKeys.AutoStart, "0");

        Assert.Null(RunValue());
    }

    [Fact]
    public void Reconcile_RepairsAMissingEntry_WhenSettingIsOn()
    {
        _settings.SetApp(AppSettingKeys.AutoStart, "1"); // before service exists
        using var service = Create();
        Assert.Null(RunValue()); // nothing listened yet

        service.Reconcile();

        Assert.NotNull(RunValue());
    }

    [Fact]
    public void Reconcile_RemovesAStaleEntry_WhenSettingIsOff()
    {
        _registry.WriteString(AutoStartService.RunKeyPath, AutoStartService.RunValueName, "stale");
        using var service = Create();

        service.Reconcile();

        Assert.Null(RunValue());
    }
}
