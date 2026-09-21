using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ThisIsMyPC.Core.Hardware.Sensors;

namespace ThisIsMyPC.App.ViewModels;

/// <summary>Owns read-only sampling and bounded history for one visit to Monitoring.</summary>
public sealed partial class MonitoringSensorsViewModel : ViewModelBase, IDisposable
{
    private readonly IHardwareSensorBackend _backend;
    private readonly HardwareSensorHistory _history = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly TimeSpan _staleAfter;
    private int _disposed;

    public IReadOnlyList<MonitoringComponentViewModel> Components { get; } =
    [
        new(HardwareSensorComponent.Memory, "Memory"),
        new(HardwareSensorComponent.Gpu, "GPU"),
        new(HardwareSensorComponent.Battery, "Battery"),
    ];

    [ObservableProperty] private MonitoringComponentViewModel _selectedComponent;
    [ObservableProperty] private string _statusText = "Reading available sensors...";
    [ObservableProperty] private string _coverageNotes = "Read-only monitoring. Available sensors depend on the hardware and driver.";
    [ObservableProperty] private string _updatedText = "No readings yet";
    [ObservableProperty] private bool _isStale;

    /// <summary>The serial sampling loop. Completed immediately when automatic sampling is disabled.</summary>
    public Task SamplingTask { get; }
    /// <summary>Completes after the backend releases its native resources, away from the UI thread.</summary>
    public Task DisposalTask { get; private set; } = Task.CompletedTask;

    public MonitoringSensorsViewModel(IHardwareSensorBackend backend, bool autoStart = true, TimeSpan? staleAfter = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(5);
        if (_staleAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleAfter));
        _backend = backend;
        _selectedComponent = Components[0];
        SamplingTask = autoStart ? Task.Run(SampleLoopAsync) : Task.CompletedTask;
    }

    private async Task SampleLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await SampleOnceAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    /// <summary>Reads once through the same serial gate as automatic sampling.</summary>
    public async Task SampleOnceAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        await _readGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            HardwareSensorSnapshot snapshot;
            string? failure = null;
            try
            {
                // Backends may do synchronous setup before returning a task.
                var reading = Task.Run(() => _backend.ReadAsync(_stop.Token), _stop.Token);
                using var ageTimer = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                var stale = Task.Delay(_staleAfter, ageTimer.Token);
                if (await Task.WhenAny(reading, stale).ConfigureAwait(false) != reading)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (Volatile.Read(ref _disposed) != 0) return;
                        IsStale = true;
                        StatusText = "Readings are stale. Waiting for the sensor refresh to finish.";
                    }, DispatcherPriority.Normal, _stop.Token);
                }
                else ageTimer.Cancel();
                // Keep the serial gate while waiting. Age reporting never starts another read.
                snapshot = await reading.WaitAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failure = ex.Message;
                snapshot = new(DateTimeOffset.UtcNow, [], ["Sensor refresh failed. " + failure]);
            }

            _stop.Token.ThrowIfCancellationRequested();
            var statistics = _history.Update(snapshot);
            var failed = failure is not null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                foreach (var component in Components) component.Update(statistics);
                IsStale = failed || DateTimeOffset.UtcNow - snapshot.CapturedAt > _staleAfter;
                StatusText = failed ? "Sensor refresh failed. Waiting to retry."
                    : IsStale ? "Readings are stale. Waiting for fresh data."
                    : statistics.Any(s => s.Current.HasValue) ? "Live readings. Refreshes about once a second."
                    : "No sensor readings are available.";
                UpdatedText = failed ? "Latest refresh failed" : "Read at " + snapshot.CapturedAt.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
                CoverageNotes = snapshot.CoverageNotes.Count > 0
                    ? string.Join(Environment.NewLine, snapshot.CoverageNotes)
                    : "Read-only monitoring. Only sensors available on this PC are shown.";
            }, DispatcherPriority.Normal, _stop.Token);
        }
        finally { _readGate.Release(); }
    }

    /// <summary>Stops sampling immediately and releases the backend without blocking navigation.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        DisposalTask = Task.Run(async () =>
        {
            await SamplingTask.ConfigureAwait(false);
            await _readGate.WaitAsync().ConfigureAwait(false);
            try { _backend.Dispose(); }
            finally { _readGate.Release(); }
        });
    }
}

/// <summary>A stable component tab, including unavailable components.</summary>
public sealed class MonitoringComponentViewModel(HardwareSensorComponent component, string name) : ObservableObject
{
    public HardwareSensorComponent Component { get; } = component;
    public string Name { get; } = name;
    public ObservableCollection<MonitoringSensorRowViewModel> Sensors { get; } = [];
    public bool IsEmpty => Sensors.Count == 0;
    public string EmptyMessage => $"No {Name.ToLowerInvariant()} sensors are available on this PC.";

    internal void Update(IReadOnlyList<HardwareSensorStatistics> statistics)
    {
        var matching = statistics.Where(s => s.Reading.Component == Component).ToArray();
        var ids = matching.Select(s => s.Reading.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = Sensors.Count - 1; i >= 0; i--)
            if (!ids.Contains(Sensors[i].Id)) Sensors.RemoveAt(i);
        foreach (var sensor in matching)
        {
            var row = Sensors.FirstOrDefault(s => s.Id == sensor.Reading.Id);
            if (row is null) Sensors.Add(new(sensor));
            else row.Update(sensor);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>One sensor's latest reading and statistics over its retained samples.</summary>
public sealed partial class MonitoringSensorRowViewModel(HardwareSensorStatistics statistics) : ObservableObject
{
    private HardwareSensorStatistics _statistics = statistics;
    public string Id => _statistics.Reading.Id;
    public string Name => _statistics.Reading.Name;
    public string DeviceName => _statistics.Reading.DeviceName;
    public string Current => Format(_statistics.Current);
    public string Minimum => "Minimum " + Format(_statistics.Minimum);
    public string Maximum => "Maximum " + Format(_statistics.Maximum);
    public string Average => "Average " + Format(_statistics.Average);
    public IReadOnlyList<HardwareSensorSample> History => _statistics.History;
    public bool IsUnavailable => !_statistics.Current.HasValue;
    [ObservableProperty] private bool _isExpanded;

    internal void Update(HardwareSensorStatistics statistics)
    {
        _statistics = statistics;
        OnPropertyChanged(string.Empty);
    }

    private string Format(double? value)
    {
        if (value is not { } number || !double.IsFinite(number)) return "Unavailable";
        var unit = _statistics.Reading.Unit switch
        {
            HardwareSensorUnit.Celsius => "°C", HardwareSensorUnit.Percent => "%",
            HardwareSensorUnit.Megabytes => "MiB", HardwareSensorUnit.Gigabytes => "GiB",
            HardwareSensorUnit.Megahertz => "MHz", HardwareSensorUnit.Watts => "W",
            HardwareSensorUnit.Rpm => "RPM", HardwareSensorUnit.Volts => "V",
            HardwareSensorUnit.BytesPerSecond => "B/s", HardwareSensorUnit.MilliwattHours => "mWh",
            HardwareSensorUnit.Milliamperes => "mA", HardwareSensorUnit.Hours => "h", _ => "",
        };
        return number.ToString("0.##", CultureInfo.CurrentCulture) + (unit.Length == 0 ? "" : " " + unit);
    }
}
