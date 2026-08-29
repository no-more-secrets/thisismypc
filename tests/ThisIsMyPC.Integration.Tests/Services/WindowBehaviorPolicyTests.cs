using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.Integration.Tests.Services;

public class WindowBehaviorPolicyTests
{
    [Theory]
    // Defaults: full terminate, zero background footprint
    [InlineData(false, "exit", CloseDecision.Terminate)]
    [InlineData(true, "exit", CloseDecision.Terminate)]
    // Tray close only when tray mode is on — otherwise fall back to terminate
    [InlineData(true, "tray", CloseDecision.HideToTray)]
    [InlineData(false, "tray", CloseDecision.Terminate)]
    // Taskbar close is tray-independent
    [InlineData(false, "taskbar", CloseDecision.MinimizeToTaskbar)]
    [InlineData(true, "taskbar", CloseDecision.MinimizeToTaskbar)]
    // Unknown/corrupt value degrades to the safe default
    [InlineData(true, "banana", CloseDecision.Terminate)]
    public void DecideClose_Matrix(bool trayMode, string closeAction, CloseDecision expected)
        => Assert.Equal(expected, WindowBehaviorPolicy.DecideClose(trayMode, closeAction));

    [Theory]
    [InlineData(false, "taskbar", MinimizeDecision.Taskbar)]
    [InlineData(true, "taskbar", MinimizeDecision.Taskbar)]
    [InlineData(true, "tray", MinimizeDecision.HideToTray)]
    // Tray minimize without tray mode falls back to taskbar
    [InlineData(false, "tray", MinimizeDecision.Taskbar)]
    [InlineData(true, "banana", MinimizeDecision.Taskbar)]
    public void DecideMinimize_Matrix(bool trayMode, string minimizeAction, MinimizeDecision expected)
        => Assert.Equal(expected, WindowBehaviorPolicy.DecideMinimize(trayMode, minimizeAction));
}
