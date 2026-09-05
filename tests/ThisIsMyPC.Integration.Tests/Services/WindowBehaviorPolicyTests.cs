using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Integration.Tests.Services;

public class WindowBehaviorPolicyTests
{
    [Theory]
    [InlineData(false, CloseDecision.Terminate)]
    [InlineData(true, CloseDecision.HideToTray)]
    public void DecideClose_UsesTrayMode(bool trayMode, CloseDecision expected)
        => Assert.Equal(expected, WindowBehaviorPolicy.DecideClose(trayMode));

    [Fact]
    public void NormalizeLegacySettings_MakesWindowActionsConsistent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tipc-window-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Initialize();
            settings.SetApp(AppSettingKeys.TrayMode, "1");
            settings.SetApp(AppSettingKeys.CloseAction, "exit");
            settings.SetApp(AppSettingKeys.MinimizeAction, "tray");

            WindowBehaviorPolicy.NormalizeLegacySettings(settings);

            Assert.Equal("tray", settings.GetApp(AppSettingKeys.CloseAction, ""));
            Assert.Equal("taskbar", settings.GetApp(AppSettingKeys.MinimizeAction, ""));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
