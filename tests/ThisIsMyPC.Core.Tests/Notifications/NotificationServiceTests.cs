using ThisIsMyPC.Core.Notifications;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Core.Tests.Notifications;

public class NotificationServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-notify-{Guid.NewGuid():N}");
    private readonly SettingsService _settings;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        _settings.Initialize();
        _service = new NotificationService(_settings);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DefaultSettings_NotificationRaised()
    {
        AppNotification? received = null;
        _service.NotificationRaised += (_, n) => received = n;

        var raised = _service.Notify(NotificationType.Monitoring, "T", "M");

        Assert.True(raised);
        Assert.Equal(new AppNotification(NotificationType.Monitoring, "T", "M"), received);
    }

    [Fact]
    public void MasterToggleOff_NothingRaised()
    {
        _settings.SetApp(AppSettingKeys.Notifications, "0");
        var raised = false;
        _service.NotificationRaised += (_, _) => raised = true;

        Assert.False(_service.Notify(NotificationType.Monitoring, "T", "M"));
        Assert.False(_service.Notify(NotificationType.UpdateAvailable, "T", "M"));
        Assert.False(raised);
    }

    [Fact]
    public void GranularToggle_GatesOnlyItsType()
    {
        _settings.SetApp(AppSettingKeys.NotifyMonitoring, "0");
        var received = new List<NotificationType>();
        _service.NotificationRaised += (_, n) => received.Add(n.Type);

        Assert.False(_service.Notify(NotificationType.Monitoring, "T", "M"));
        Assert.True(_service.Notify(NotificationType.UpdateAvailable, "T", "M"));
        Assert.Equal([NotificationType.UpdateAvailable], received);
    }
}
