using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.Controls;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Hardware.Sensors;

namespace ThisIsMyPC.App.UiTests;

public sealed class MonitoringSensorsShotTests
{
    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task Monitoring_UsesMainWindowEdgeGeometry()
    {
        using var backend = new FakeBackend(_ => Task.FromResult(new HardwareSensorSnapshot(DateTimeOffset.UtcNow,
            Enumerable.Range(1, 18).Select(i => new HardwareSensorReading("memory-" + i, "ram", "System memory",
                HardwareSensorComponent.Memory, "Memory sensor " + i, HardwareSensorUnit.Percent, 42)).ToArray(),
            ["CPU and motherboard sensors are not available in this backend."])));
        using var vm = new MonitoringSensorsViewModel(backend, autoStart: false);
        using var session = UiSession.ForMainWindow("monitoring-host");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0);
        await vm.SampleOnceAsync();
        main.CurrentContent = vm;
        main.ContentTitle = "Monitoring";
        session.Screenshot("monitoring-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("monitoring-light");
        vm.Dispose();
        await vm.DisposalTask;
    }

    [AvaloniaFact]
    public async Task Monitoring_ShowsHistoryMissingReadingsAndEmptyBattery_InBothThemes()
    {
        var count = 0;
        var start = DateTimeOffset.UtcNow.AddSeconds(-120);
        using var backend = new FakeBackend(_ =>
        {
            var i = count++;
            double? value = i == 119 || i is >= 50 and <= 65 ? null : 35 + 12 * Math.Sin(i / 8d);
            return Task.FromResult(new HardwareSensorSnapshot(start.AddSeconds(i),
            [
                new("memory-load", "ram", "System memory", HardwareSensorComponent.Memory, "Memory used", HardwareSensorUnit.Percent, value),
                new("memory-free", "ram", "System memory", HardwareSensorComponent.Memory, "Available memory", HardwareSensorUnit.Gigabytes, 18.5),
                new("gpu-temp", "gpu", "Example graphics card", HardwareSensorComponent.Gpu, "GPU temperature", HardwareSensorUnit.Celsius, 43),
                new("gpu-fan", "gpu", "Example graphics card", HardwareSensorComponent.Gpu, "Fan speed", HardwareSensorUnit.Rpm, 0),
            ], ["CPU and motherboard sensors are not available in this backend."]));
        });
        using var vm = new MonitoringSensorsViewModel(backend, autoStart: false);
        using var session = UiSession.ForView(new MonitoringSensorsView(), vm, "monitoring-sensors");
        for (var i = 0; i < 120; i++) await vm.SampleOnceAsync();
        var row = vm.Components[0].Sensors[0];
        Assert.Equal("18.5 GiB", vm.Components[0].Sensors[1].Current);
        Assert.Equal("Unavailable", row.Current);
        Assert.Contains(row.History, sample => sample.Value is null);
        Assert.DoesNotContain(row.History, sample => sample.Value == 0);
        row.IsExpanded = true;
        session.Screenshot("memory-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("memory-light");
        session.ClickText("GPU");
        Assert.Equal(HardwareSensorComponent.Gpu, vm.SelectedComponent.Component);
        Assert.Contains(vm.SelectedComponent.Sensors, sensor => sensor.Current == "0 RPM");
        session.Screenshot("gpu-light");
        session.ClickText("Battery");
        Assert.True(vm.SelectedComponent.IsEmpty);
        Assert.True(session.IsTextVisible("No battery sensors are available on this PC."));
        session.Screenshot("battery-empty");
        vm.Dispose();
        await vm.DisposalTask;
    }

    [AvaloniaFact]
    public async Task RefreshFailure_ExposesUnavailableCurrentAndRetainsHistory()
    {
        var count = 0;
        using var backend = new FakeBackend(_ => count++ == 0
            ? Task.FromResult(new HardwareSensorSnapshot(DateTimeOffset.UtcNow.AddSeconds(-1),
                [new("memory", "ram", "System memory", HardwareSensorComponent.Memory, "Memory used", HardwareSensorUnit.Percent, 42)], []))
            : throw new IOException("Example sensor connection ended."));
        using var vm = new MonitoringSensorsViewModel(backend, autoStart: false);
        using var session = UiSession.ForView(new MonitoringSensorsView(), vm, "monitoring-failure");
        await vm.SampleOnceAsync();
        await vm.SampleOnceAsync();
        Assert.True(vm.IsStale);
        Assert.Equal("Unavailable", vm.Components[0].Sensors[0].Current);
        Assert.Contains("42", vm.Components[0].Sensors[0].Minimum, StringComparison.Ordinal);
        Assert.Null(vm.Components[0].Sensors[0].History[^1].Value);
        Assert.Contains("failed", vm.StatusText, StringComparison.Ordinal);
        session.Screenshot("failed-refresh");
        vm.Dispose();
        await vm.DisposalTask;
    }

    [AvaloniaFact]
    public async Task Sampling_IsSerial_WhenManualSamplesOverlap()
    {
        var active = 0;
        var maximum = 0;
        using var backend = new FakeBackend(async token =>
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            await Task.Delay(30, token);
            Interlocked.Decrement(ref active);
            return new(DateTimeOffset.UtcNow, [], []);
        });
        using var vm = new MonitoringSensorsViewModel(backend, autoStart: false);
        await Task.WhenAll(vm.SampleOnceAsync(), vm.SampleOnceAsync(), vm.SampleOnceAsync());
        Assert.Equal(1, maximum);
        vm.Dispose();
        await vm.DisposalTask;
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task NavigationDisposal_CancelsSampling_AndDoesNotBlockUi()
    {
        var backend = new BlockingBackend();
        using var vm = new MonitoringSensorsViewModel(backend);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var uiThread = Environment.CurrentManagedThreadId;
        try
        {
            vm.Dispose();
            await backend.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(vm.DisposalTask.IsCompleted);
            Assert.NotEqual(uiThread, backend.DisposalThread);
            Assert.True(vm.SamplingTask.IsCompleted);
            Assert.Equal(1, backend.ReadCount);
        }
        finally { backend.ReleaseDispose.TrySetResult(); }
        await vm.DisposalTask.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void HistoryGraph_ExtremeFiniteReadings_KeepCoordinatesFinite()
    {
        var time = DateTimeOffset.UtcNow;
        var segments = SensorHistoryGraph.CreateSegments(
            [new(time, -double.MaxValue), new(time.AddSeconds(1), 0), new(time.AddSeconds(2), double.MaxValue)],
            new Size(240, 64));
        var segment = Assert.Single(segments);
        Assert.Equal(3, segment.Length);
        Assert.All(segment, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y)));
        Assert.True(segment[0].Y > segment[1].Y && segment[1].Y > segment[2].Y);
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task BlockedRefresh_MarksPriorReadingsStale_WithoutAnotherRead()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HardwareSensorSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        using var backend = new FakeBackend(token =>
        {
            if (Interlocked.Increment(ref reads) == 1)
                return Task.FromResult(new HardwareSensorSnapshot(DateTimeOffset.UtcNow,
                    [new("memory", "ram", "System memory", HardwareSensorComponent.Memory, "Memory used", HardwareSensorUnit.Percent, 42)], []));
            entered.TrySetResult();
            return release.Task.WaitAsync(token);
        });
        using var vm = new MonitoringSensorsViewModel(backend, autoStart: false, staleAfter: TimeSpan.FromMilliseconds(100));
        using var session = UiSession.ForView(new MonitoringSensorsView(), vm, "monitoring-stale");
        await vm.SampleOnceAsync();
        Assert.False(vm.IsStale);
        var blocked = vm.SampleOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await session.WaitForAsync(() => vm.IsStale, timeoutMs: 3000);
        Assert.False(blocked.IsCompleted);
        Assert.Equal(2, reads);
        Assert.Contains("stale", vm.StatusText, StringComparison.Ordinal);
        session.Screenshot("blocked-refresh");
        release.SetResult(new(DateTimeOffset.UtcNow,
            [new("memory", "ram", "System memory", HardwareSensorComponent.Memory, "Memory used", HardwareSensorUnit.Percent, 43)], []));
        await blocked;
        Assert.False(vm.IsStale);
        Assert.Equal("43 %", vm.Components[0].Sensors[0].Current);
        vm.Dispose();
        await vm.DisposalTask;
    }

    [Fact]
    public void HistoryGraph_DoesNotConnectAcrossMissingSamples()
    {
        var time = DateTimeOffset.UtcNow;
        var segments = SensorHistoryGraph.CreateSegments(
            [new(time, 10), new(time.AddSeconds(1), 20), new(time.AddSeconds(2), null),
                new(time.AddSeconds(3), 30), new(time.AddSeconds(4), 40)], new Size(240, 64));
        Assert.Equal(2, segments.Count);
        Assert.All(segments, segment => Assert.Equal(2, segment.Length));
        Assert.True(segments[0][^1].X < segments[1][0].X);
    }

    private sealed class FakeBackend(Func<CancellationToken, Task<HardwareSensorSnapshot>> read) : IHardwareSensorBackend
    {
        public Task<HardwareSensorSnapshot> ReadAsync(CancellationToken cancellationToken = default) => read(cancellationToken);
        public void Dispose() { }
    }

    private sealed class BlockingBackend : IHardwareSensorBackend
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount { get; private set; }
        public int DisposalThread { get; private set; }
        public async Task<HardwareSensorSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancelled reads never return data.");
        }
        public void Dispose()
        {
            DisposalThread = Environment.CurrentManagedThreadId;
            Disposing.TrySetResult();
            ReleaseDispose.Task.GetAwaiter().GetResult();
        }
    }
}
