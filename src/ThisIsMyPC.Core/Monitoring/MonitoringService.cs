using System.Text.Json;
using System.Text.Json.Serialization;
using ThisIsMyPC.Core.Notifications;
using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Core.Monitoring;

/// <summary>
/// One monitored boot-sequence item. <see cref="Id"/> doubles as the
/// "Startup &amp; Services" set settingId (service-starttype:/scheduled-task:/
/// startup-entry: conventions) so a detection can be disabled through the existing
/// inspector plumbing.
/// </summary>
public sealed record MonitorItem(string Id, string DisplayName, string Source);

public interface IMonitoringSnapshotProvider
{
    /// <summary>Current startup entries + services + scheduled tasks. Failures degrade to omissions.</summary>
    IReadOnlyList<MonitorItem> Capture();
}

public sealed class MonitoringDetection
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string Source { get; set; }
    public required DateTimeOffset DetectedAt { get; set; }
    public bool IsReviewed { get; set; }
}

public sealed class MonitoringState
{
    public bool BaselineCaptured { get; set; }
    public List<string>? Baseline { get; set; }
    public List<MonitoringDetection>? Detections { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(MonitoringState))]
public sealed partial class MonitoringJsonContext : JsonSerializerContext;

/// <summary>
/// Opt-in boot-sequence monitoring (9-3). Runs ONLY while the app is in memory — no
/// hidden services or scheduled tasks, ever (the Epic 28 service covers the
/// closed-app case later). First enable captures a baseline without flagging
/// anything; subsequent scans flag additions, persist them, and raise a gated
/// Monitoring notification per new item.
/// </summary>
public sealed class MonitoringService : IDisposable
{
    public static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);

    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IMonitoringSnapshotProvider _provider;
    private readonly string _statePath;
    private readonly Lock _sync = new();

    private HashSet<string> _baseline = new(StringComparer.OrdinalIgnoreCase);
    private List<MonitoringDetection> _detections = [];
    private bool _baselineCaptured;
    private bool _stateLoaded;
    private CancellationTokenSource? _loop;

    public MonitoringService(
        ISettingsService settings,
        INotificationService notifications,
        IMonitoringSnapshotProvider provider,
        string? statePath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(provider);
        _settings = settings;
        _notifications = notifications;
        _provider = provider;
        _statePath = statePath ?? Path.Combine(AppConstants.DataDirectoryPath, "monitoring.json");

        _settings.SettingChanged += OnSettingChanged;
    }

    public event EventHandler? DetectionsChanged;

    public IReadOnlyList<MonitoringDetection> UnreviewedDetections
    {
        get
        {
            lock (_sync)
            {
                EnsureStateLoaded();
                return _detections.Where(d => !d.IsReviewed).ToList();
            }
        }
    }

    /// <summary>Call once at startup: begins the loop when the setting is already on.</summary>
    public void Start()
    {
        if (_settings.GetAppBool(AppSettingKeys.MonitoringEnabled, fallback: false))
            StartLoop();
    }

    public void MarkReviewed(string detectionId)
    {
        lock (_sync)
        {
            EnsureStateLoaded();
            var detection = _detections.FirstOrDefault(d => d.Id == detectionId && !d.IsReviewed);
            if (detection is null)
                return;
            detection.IsReviewed = true;
            SaveState();
        }
        DetectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One scan cycle (also the test seam). No-op when monitoring is off.</summary>
    public void CheckOnce()
    {
        if (!_settings.GetAppBool(AppSettingKeys.MonitoringEnabled, fallback: false))
            return;

        List<MonitoringDetection> fresh = [];
        lock (_sync)
        {
            EnsureStateLoaded();
            var current = _provider.Capture();

            if (!_baselineCaptured)
            {
                // First enable: everything already present is the baseline, not news.
                foreach (var item in current)
                    _baseline.Add(item.Id);
                _baselineCaptured = true;
                SaveState();
                return;
            }

            foreach (var item in current)
            {
                if (_baseline.Add(item.Id))
                {
                    fresh.Add(new MonitoringDetection
                    {
                        Id = item.Id,
                        DisplayName = item.DisplayName,
                        Source = item.Source,
                        DetectedAt = DateTimeOffset.UtcNow,
                    });
                }
            }

            if (fresh.Count > 0)
            {
                _detections.AddRange(fresh);
                SaveState();
            }
        }

        foreach (var detection in fresh)
        {
            _notifications.Notify(
                NotificationType.Monitoring,
                "New startup item detected",
                $"{detection.DisplayName} from {detection.Source}");
        }
        if (fresh.Count > 0)
            DetectionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e is not { Scope: SettingChangedEventArgs.AppScope, Key: AppSettingKeys.MonitoringEnabled })
            return;

        if (e.Value == "1")
            StartLoop();
        else
            StopLoop();
    }

    private void StartLoop()
    {
        lock (_sync)
        {
            if (_loop is not null)
                return;
            _loop = new CancellationTokenSource();
        }

        var token = _loop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                CheckOnce(); // immediate scan on enable (captures baseline first time)
                using var timer = new PeriodicTimer(ScanInterval);
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    CheckOnce();
            }
            catch (OperationCanceledException)
            {
                // disable/shutdown — expected
            }
        }, CancellationToken.None);
    }

    private void StopLoop()
    {
        CancellationTokenSource? loop;
        lock (_sync)
        {
            loop = _loop;
            _loop = null;
        }
        loop?.Cancel();
        loop?.Dispose();
    }

    private void EnsureStateLoaded()
    {
        if (_stateLoaded)
            return;
        _stateLoaded = true;

        try
        {
            if (!File.Exists(_statePath))
                return;
            var state = JsonSerializer.Deserialize(
                File.ReadAllText(_statePath), MonitoringJsonContext.Default.MonitoringState);
            _baseline = new HashSet<string>(state?.Baseline ?? [], StringComparer.OrdinalIgnoreCase);
            _detections = state?.Detections ?? [];
            _baselineCaptured = state?.BaselineCaptured ?? false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt/unreadable state: start fresh (next scan rebuilds the baseline).
            _baseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _detections = [];
            _baselineCaptured = false;
        }
    }

    private void SaveState()
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(
                new MonitoringState { BaselineCaptured = _baselineCaptured, Baseline = [.. _baseline], Detections = _detections },
                MonitoringJsonContext.Default.MonitoringState);
            var tempPath = _statePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _statePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence; in-memory state still applies this session.
        }
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        StopLoop();
    }
}
