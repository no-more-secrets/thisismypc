using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Settings;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.UiTests;

public sealed class OwnerModeStatusShotTests
{
    private sealed class ServiceControl : IOwnerModeServiceControl
    {
        public RestorationStatusResponse Status { get; set; } = new()
        {
            State = RestorationServiceState.Enabled,
            ConsentGranted = true,
            Detail = "Protected settings match your choices.",
        };
        public int StatusReads { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public event EventHandler? StateChanged { add { } remove { } }
        public OwnerModeState GetState() => OwnerModeState.Running;
        public Task<RestorationStatusResponse> GetRestorationStatusAsync(CancellationToken cancellationToken = default)
        {
            StatusReads++;
            LastToken = cancellationToken;
            return Task.FromResult(Status);
        }
        public Task<OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This test only reads status.");
        public Task<OperationResult<bool>> DisableAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This test only reads status.");
    }

    [Fact]
    public async Task Wrapper_ForwardsLiveStatusAndCancellation_UnlessServiceStateIsSimulated()
    {
        var real = new ServiceControl();
        var simulation = new DebugSimulation();
        IOwnerModeServiceControl wrapper = new SimulatedOwnerModeControl(real, simulation);
        using var cancellation = new CancellationTokenSource();
        Assert.Same(real.Status, await wrapper.GetRestorationStatusAsync(cancellation.Token));
        Assert.Equal(cancellation.Token, real.LastToken);
        simulation.Sku = Core.Modules.WindowsSku.Home;
        Assert.Same(real.Status, await wrapper.GetRestorationStatusAsync());
        var reads = real.StatusReads;
        simulation.OwnerModeState = OwnerModeState.Stopped;
        var status = await wrapper.GetRestorationStatusAsync();
        Assert.Equal(RestorationServiceState.Unavailable, status.State);
        Assert.False(status.ConsentGranted);
        Assert.Contains("simulated", status.Detail, StringComparison.Ordinal);
        Assert.Equal(reads, real.StatusReads);
        simulation.Reset();
        Assert.Same(real.Status, await wrapper.GetRestorationStatusAsync());
    }

    [AvaloniaFact]
    public async Task Settings_ShowsLiveRestorationThroughWrapper_AndRefreshesChanges()
    {
        var real = new ServiceControl();
        var section = new OwnerModeSectionViewModel(new SimulatedOwnerModeControl(real, new DebugSimulation()));
        await section.Initialization;
        var settings = new SettingsViewModel(
            new SettingsService(Path.Combine(Path.GetTempPath(), $"tipc-owner-status-{Guid.NewGuid():N}.json")),
            [], ownerMode: section);
        using var session = UiSession.ForView(new SettingsView(), settings, "owner-mode-status", width: 1200, height: 800);
        session.ClickText("Owner Mode");
        Assert.True(session.IsTextVisible("Enabled"));
        Assert.True(session.IsTextVisible(real.Status.Detail));
        Assert.True(section.IsRestorationEnabled);
        session.Screenshot("enabled");

        real.Status = new() { State = RestorationServiceState.Paused, Detail = "Restoration is paused." };
        session.ClickText("Refresh");
        await session.WaitForAsync(() => section.StateText == "Paused", what: "live paused status");
        Assert.False(section.IsRestorationEnabled);
        Assert.True(session.IsTextVisible(real.Status.Detail));
        session.Screenshot("paused");
    }
}
