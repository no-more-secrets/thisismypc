using ThisIsMyPC.Core.Monitoring;
using ThisIsMyPC.Core.Notifications;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Core.Tests.Monitoring;

public sealed class MonitoringServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-monitor-{Guid.NewGuid():N}");
    private readonly SettingsService _settings;
    private readonly NotificationService _notifications;

    private sealed class StubProvider : IMonitoringSnapshotProvider
    {
        public List<MonitorItem> Items { get; } = [];
        public IReadOnlyList<MonitorItem> Capture() => Items.ToList();
    }

    private readonly StubProvider _provider = new();

    public MonitoringServiceTests()
    {
        _settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        _settings.Initialize();
        _settings.SetApp(AppSettingKeys.MonitoringEnabled, "1");
        _notifications = new NotificationService(_settings);
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

    private MonitoringService Create() =>
        new(_settings, _notifications, _provider, Path.Combine(_dir, "monitoring.json"));

    private static MonitorItem Service(string name) =>
        new($"service-starttype:{name}", name, "service");

    [Fact]
    public void FirstScan_CapturesBaseline_NoDetections()
    {
        _provider.Items.Add(Service("Existing"));
        using var monitor = Create();

        monitor.CheckOnce();

        Assert.Empty(monitor.UnreviewedDetections);
    }

    [Fact]
    public void NewItemAfterBaseline_Detected_Notified_Persisted()
    {
        _provider.Items.Add(Service("Existing"));
        using var monitor = Create();
        monitor.CheckOnce();

        var notified = new List<AppNotification>();
        _notifications.NotificationRaised += (_, n) => notified.Add(n);
        _provider.Items.Add(Service("SneakyNewService"));

        monitor.CheckOnce();

        var detection = Assert.Single(monitor.UnreviewedDetections);
        Assert.Equal("service-starttype:SneakyNewService", detection.Id);
        Assert.Equal("service", detection.Source);
        var notification = Assert.Single(notified);
        Assert.Equal(NotificationType.Monitoring, notification.Type);
        Assert.Contains("SneakyNewService", notification.Message, StringComparison.Ordinal);

        // Survives a restart (new instance, same state file)
        using var second = Create();
        Assert.Single(second.UnreviewedDetections);
    }

    [Fact]
    public void SameItem_NeverDetectedTwice()
    {
        using var monitor = Create();
        monitor.CheckOnce(); // empty baseline exists after this? (no items) — add then scan twice
        _provider.Items.Add(Service("New"));
        monitor.CheckOnce();
        monitor.CheckOnce();

        Assert.Single(monitor.UnreviewedDetections);
    }

    [Fact]
    public void MarkReviewed_RemovesFromUnreviewed_AndPersists()
    {
        using var monitor = Create();
        monitor.CheckOnce();
        _provider.Items.Add(Service("New"));
        monitor.CheckOnce();
        var detection = Assert.Single(monitor.UnreviewedDetections);

        monitor.MarkReviewed(detection.Id);

        Assert.Empty(monitor.UnreviewedDetections);
        using var second = Create();
        Assert.Empty(second.UnreviewedDetections);
    }

    [Fact]
    public void MonitoringDisabled_CheckOnceIsANoOp()
    {
        _settings.SetApp(AppSettingKeys.MonitoringEnabled, "0");
        _provider.Items.Add(Service("Anything"));
        using var monitor = Create();

        monitor.CheckOnce();
        _provider.Items.Add(Service("More"));
        monitor.CheckOnce();

        Assert.Empty(monitor.UnreviewedDetections);
    }

    [Fact]
    public void DetectionsChanged_RaisedOnDetectionAndReview()
    {
        using var monitor = Create();
        monitor.CheckOnce();
        var raised = 0;
        monitor.DetectionsChanged += (_, _) => raised++;

        _provider.Items.Add(Service("New"));
        monitor.CheckOnce();
        Assert.Equal(1, raised);

        monitor.MarkReviewed(monitor.UnreviewedDetections[0].Id);
        Assert.Equal(2, raised);
    }
}
