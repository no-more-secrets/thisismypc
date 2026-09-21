using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Hardware.Sensors;

namespace ThisIsMyPC.App.UiTests;

[Trait("Category", "Diagnostic")]
public sealed class MonitoringNavigationTests
{
    [AvaloniaFact(Timeout = 60_000)]
    public async Task MonitoringCreatesBackendOnEntryAndDisposesOnExit()
    {
        var backends = new List<FakeBackend>();
        using var session = UiSession.ForMainWindow("monitoring-navigation", services =>
        {
            services.RemoveAll<Func<IHardwareSensorBackend>>();
            services.AddSingleton<Func<IHardwareSensorBackend>>(_ => () =>
            {
                var backend = new FakeBackend();
                backends.Add(backend);
                return backend;
            });
        });
        var main = Assert.IsType<MainWindowViewModel>(session.Window.DataContext);
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, what: "sidebar population");
        Assert.Empty(backends);

        session.ScrollAndClickText("Monitoring");
        await session.WaitForAsync(() => main.CurrentContent is MonitoringSensorsViewModel sensors
            && sensors.Components[0].Sensors.Count == 1, what: "live monitoring reading");
        var monitoring = Assert.IsType<MonitoringSensorsViewModel>(main.CurrentContent);
        Assert.True(main.UsesEdgeTabs);
        Assert.Contains("Physical memory load", session.DescribeVisibleText());
        session.Screenshot("live-monitoring");

        session.ClickText("Settings");
        await session.WaitForAsync(() => main.ContentTitle == "Settings", what: "leave monitoring");
        await session.WaitForAsync(() => monitoring.DisposalTask.IsCompleted, what: "sensor disposal");
        await monitoring.DisposalTask;
        Assert.True(Assert.Single(backends).Disposed);
        Assert.True(monitoring.SamplingTask.IsCompletedSuccessfully);

        session.ScrollAndClickText("Monitoring");
        await session.WaitForAsync(() => main.CurrentContent is MonitoringSensorsViewModel sensors
            && sensors.Components[0].Sensors.Count == 1, what: "fresh monitoring visit");
        Assert.Equal(2, backends.Count);
        var secondVisit = Assert.IsType<MonitoringSensorsViewModel>(main.CurrentContent);
        await main.PrepareForShutdownAsync();
        await session.WaitForAsync(() => secondVisit.DisposalTask.IsCompleted, what: "shutdown sensor disposal");
        await secondVisit.DisposalTask;
        Assert.True(backends[1].Disposed);
    }

    private sealed class FakeBackend : IHardwareSensorBackend
    {
        public bool Disposed { get; private set; }
        public Task<HardwareSensorSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HardwareSensorSnapshot(DateTimeOffset.UtcNow,
                [new("memory/load", "memory", "System memory", HardwareSensorComponent.Memory,
                    "Physical memory load", HardwareSensorUnit.Percent, 42)], []));
        }
        public void Dispose() => Disposed = true;
    }
}
