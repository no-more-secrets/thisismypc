using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.App.UiTests;

public sealed class WindowPersistenceTests
{
    [AvaloniaFact]
    public void Close_HidesWindow_WhenTrayModeAndIconAreAvailable()
    {
        using var context = TestContext.Create(trayMode: true, trayAvailable: true);

        context.Session.Window.Close();
        context.Session.Pump();

        Assert.False(context.Session.Window.IsVisible);
    }

    [AvaloniaFact]
    public void Close_Continues_WhenTrayIconIsUnavailable()
    {
        using var context = TestContext.Create(trayMode: true, trayAvailable: false);
        var canceled = true;
        context.Session.Window.Closing += (_, e) => canceled = e.Cancel;

        context.Session.Window.Close();

        Assert.False(canceled);
    }

    [AvaloniaFact]
    public void Minimize_RemainsVisibleOnTaskbar()
    {
        using var context = TestContext.Create(trayMode: true, trayAvailable: true);

        context.Session.Window.WindowState = WindowState.Minimized;
        context.Session.Pump();

        Assert.True(context.Session.Window.IsVisible);
        Assert.Equal(WindowState.Minimized, context.Session.Window.WindowState);
    }

    [AvaloniaFact]
    public void DisablingTrayMode_RestoresHiddenWindow()
    {
        using var context = TestContext.Create(trayMode: true, trayAvailable: true);
        context.Session.Window.Hide();

        context.Settings.SetApp(AppSettingKeys.TrayMode, "0");
        context.Session.Pump();

        Assert.True(context.Session.Window.IsVisible);
        Assert.Equal(WindowState.Normal, context.Session.Window.WindowState);
    }

    [AvaloniaFact]
    public void QueuedTrayDisable_DoesNotReopenAfterDispose()
    {
        using var context = TestContext.Create(trayMode: true, trayAvailable: true);
        context.Session.Window.Hide();

        context.Settings.SetApp(AppSettingKeys.TrayMode, "0");
        context.Controller.Dispose();
        context.Session.Pump();

        Assert.False(context.Session.Window.IsVisible);
    }

    [AvaloniaFact]
    public void QueuedTrayDisable_DoesNotReopenAfterExitRequest()
    {
        using var context = TestContext.Create(trayMode: true, trayAvailable: true);
        context.Session.Window.Hide();

        context.Settings.SetApp(AppSettingKeys.TrayMode, "0");
        context.Controller.RequestExit();
        context.Session.Pump();

        Assert.False(context.Session.Window.IsVisible);
        Assert.Equal(1, context.Lifetime.ShutdownCount);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string settingsDirectory;

        private TestContext(
            UiSession session,
            SettingsService settings,
            FakeDesktopLifetime lifetime,
            WindowPersistenceController controller,
            string settingsDirectory)
        {
            Session = session;
            Settings = settings;
            Lifetime = lifetime;
            Controller = controller;
            this.settingsDirectory = settingsDirectory;
        }

        public UiSession Session { get; }
        public SettingsService Settings { get; }
        public FakeDesktopLifetime Lifetime { get; }
        public WindowPersistenceController Controller { get; }

        public static TestContext Create(bool trayMode, bool trayAvailable)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tipc-window-ui-{Guid.NewGuid():N}");
            var settings = new SettingsService(Path.Combine(directory, "settings.json"));
            settings.Initialize();
            settings.SetApp(AppSettingKeys.TrayMode, trayMode ? "1" : "0");
            var session = UiSession.ForView(new Border(), new object(), "window-persistence", 500, 300);
            var lifetime = new FakeDesktopLifetime();
            var controller = new WindowPersistenceController(
                session.Window, lifetime.Shutdown, settings, () => trayAvailable);
            return new TestContext(session, settings, lifetime, controller, directory);
        }

        public void Dispose()
        {
            Controller.Dispose();
            Session.Dispose();
            try
            {
                if (Directory.Exists(settingsDirectory))
                    Directory.Delete(settingsDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FakeDesktopLifetime
    {
        public int ShutdownCount { get; private set; }
        public void Shutdown() => ShutdownCount++;
    }
}