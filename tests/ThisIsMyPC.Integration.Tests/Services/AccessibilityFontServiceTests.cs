using Avalonia.Controls;
using Avalonia.Media;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Integration.Tests.Services;

public sealed class AccessibilityFontServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-font-{Guid.NewGuid():N}");
    private readonly SettingsService _settings;
    private readonly ResourceDictionary _resources = [];

    public AccessibilityFontServiceTests()
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

    private AccessibilityFontService Create() => new(_settings, _resources, dispatch: a => a());

    private FontFamily? Override() =>
        _resources.TryGetValue(AccessibilityFontService.BodyFontKey, out var value)
            ? value as FontFamily : null;

    [Fact]
    public void Default_NoOverrideInstalled()
    {
        using var service = Create();
        Assert.Null(Override());
    }

    [Fact]
    public void SettingAlreadyOnAtStartup_InstallsOverrideImmediately()
    {
        _settings.SetApp(AppSettingKeys.DyslexiaFont, "1");
        using var service = Create();

        Assert.Equal("OpenDyslexic", Override()?.Name);
    }

    [Fact]
    public void EnablingTheSetting_InstallsTheOpenDyslexicOverride()
    {
        using var service = Create();

        _settings.SetApp(AppSettingKeys.DyslexiaFont, "1");

        Assert.Equal("OpenDyslexic", Override()?.Name);
    }

    [Fact]
    public void DisablingTheSetting_RemovesTheOverride()
    {
        using var service = Create();
        _settings.SetApp(AppSettingKeys.DyslexiaFont, "1");
        Assert.NotNull(Override());

        _settings.SetApp(AppSettingKeys.DyslexiaFont, "0");

        Assert.Null(Override());
    }

    [Fact]
    public void AfterDispose_TogglingTheSettingDoesNothing()
    {
        var service = Create();
        service.Dispose();

        _settings.SetApp(AppSettingKeys.DyslexiaFont, "1");

        Assert.Null(Override());
    }
}
