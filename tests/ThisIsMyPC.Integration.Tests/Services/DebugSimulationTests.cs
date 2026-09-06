using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Services;

/// <summary>
/// The Debug page's simulation seam: overrides lay over the real detector and
/// service control, pass through when unset, and vanish on reset. Nothing here
/// touches a file or the registry.
/// </summary>
public sealed class DebugSimulationTests
{
    private sealed class RealDetector : ICapabilityDetector
    {
        public WindowsSku? Sku => WindowsSku.Education;
        public string? SkuDetectionFailureReason => null;
        public bool IsOwnerModeAvailable => true;
        public bool IsSkuRestricted(WindowsSku? restriction) =>
            restriction is { } required && Sku is { } current && current.Tier() < required.Tier();
        public bool IsAvailable(SystemCapability capability) => GetAvailability(capability).IsAvailable;
        public ModuleAvailability GetAvailability(SystemCapability capability) =>
            capability == SystemCapability.OpenRgb
                ? new ModuleAvailability(false, "OpenRGB not detected.", "Install OpenRGB.")
                : new ModuleAvailability(true);
        public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() =>
            [new(SystemCapability.DdcCi, "DDC", GetAvailability(SystemCapability.DdcCi)),
             new(SystemCapability.OpenRgb, "OpenRGB", GetAvailability(SystemCapability.OpenRgb))];
    }

    private sealed class RealControl : IOwnerModeServiceControl
    {
        public int EnableCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public OwnerModeState State { get; set; } = OwnerModeState.Running;
        public event EventHandler? StateChanged;
        public OwnerModeState GetState() => State;
        public Task<OperationResult<bool>> EnableAsync(CancellationToken cancellationToken = default)
        {
            EnableCalls++;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public Task<OperationResult<bool>> DisableAsync(CancellationToken cancellationToken = default)
        {
            DisableCalls++;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        public void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void Detector_PassesThrough_UntilAnOverrideIsSet_AndAgainAfterReset()
    {
        var simulation = new DebugSimulation();
        var detector = new SimulatedCapabilityDetector(new RealDetector(), simulation);
        Assert.False(simulation.IsActive);
        Assert.Equal(WindowsSku.Education, detector.Sku);
        Assert.False(detector.IsSkuRestricted(WindowsSku.Enterprise));
        Assert.False(detector.IsAvailable(SystemCapability.OpenRgb));
        Assert.True(detector.IsOwnerModeAvailable);

        simulation.Sku = WindowsSku.Home;
        Assert.True(simulation.IsActive);
        Assert.Equal(WindowsSku.Home, detector.Sku);
        Assert.True(detector.IsSkuRestricted(WindowsSku.Pro));
        Assert.True(detector.IsSkuRestricted(WindowsSku.Enterprise));
        Assert.Null(detector.SkuDetectionFailureReason);

        simulation.SetCapability(SystemCapability.OpenRgb, true);
        Assert.True(detector.IsAvailable(SystemCapability.OpenRgb));
        Assert.Contains("Simulated", detector.GetAvailability(SystemCapability.OpenRgb).RemediationHint, StringComparison.Ordinal);
        Assert.True(detector.GetCapabilityReport().Single(r => r.Capability == SystemCapability.OpenRgb).Availability.IsAvailable);

        simulation.SetCapability(SystemCapability.DdcCi, false);
        Assert.False(detector.IsAvailable(SystemCapability.DdcCi));
        Assert.Contains("simulated as missing", detector.GetAvailability(SystemCapability.DdcCi).Reason, StringComparison.Ordinal);

        simulation.OwnerModeState = OwnerModeState.Stopped;
        Assert.False(detector.IsOwnerModeAvailable);
        Assert.Equal("Windows 11 Home, Owner Mode service stopped, DDC/CI monitors missing, OpenRGB present", simulation.Summary);

        simulation.Reset();
        Assert.False(simulation.IsActive);
        Assert.Equal(string.Empty, simulation.Summary);
        Assert.Equal(WindowsSku.Education, detector.Sku);
        Assert.False(detector.IsAvailable(SystemCapability.OpenRgb));
        Assert.True(detector.IsAvailable(SystemCapability.DdcCi));
        Assert.True(detector.IsOwnerModeAvailable);
    }

    [Fact]
    public async Task Lease_RefusesWhenActive_LocksOverridesWhileHeld_AndReleasesOnDispose()
    {
        var simulation = new DebugSimulation();
        var real = new RealControl();
        var control = new SimulatedOwnerModeControl(real, simulation);

        var lease = simulation.BeginMutation();
        Assert.False(lease.IsRefused);
        Assert.True(simulation.IsMutationInFlight);
        simulation.Sku = WindowsSku.Home;
        simulation.Device = DebugSimulation.DevicePresets[0];
        simulation.OwnerModeState = OwnerModeState.NotInstalled;
        Assert.False(simulation.IsActive);
        Assert.Equal(DebugSimulation.InFlightMessage, simulation.LastRefusal);

        // A second mutation may run alongside the first; both must end before overrides return.
        var second = simulation.BeginMutation();
        Assert.False(second.IsRefused);
        lease.Dispose();
        lease.Dispose();
        Assert.True(simulation.IsMutationInFlight);
        simulation.Sku = WindowsSku.Home;
        Assert.False(simulation.IsActive);
        second.Dispose();
        Assert.False(simulation.IsMutationInFlight);

        simulation.Sku = WindowsSku.Home;
        Assert.True(simulation.IsActive);
        Assert.Null(simulation.LastRefusal);
        Assert.True(simulation.BeginMutation().IsRefused);
        Assert.Equal(DebugSimulation.BlockedMessage, simulation.BeginMutation().Refusal);
        Assert.False((await control.EnableAsync()).IsSuccess);
        Assert.Equal(0, real.EnableCalls);

        var open = MutationLease.Open();
        Assert.False(open.IsRefused);
        open.Dispose();
    }

    [Fact]
    public void Changed_FiresOncePerRealChange_AndNotForNoOps()
    {
        var simulation = new DebugSimulation();
        var fired = 0;
        simulation.Changed += (_, _) => fired++;

        simulation.Sku = WindowsSku.Pro;
        simulation.Sku = WindowsSku.Pro;
        simulation.SetCapability(SystemCapability.HwInfo, null);
        simulation.Reset();
        simulation.Reset();

        Assert.Equal(2, fired);
    }

    [Fact]
    public async Task OwnerModeControl_ReportsSimulatedState_AndRefusesRealActionsWhileSimulating()
    {
        var simulation = new DebugSimulation();
        var real = new RealControl();
        var control = new SimulatedOwnerModeControl(real, simulation);
        var changes = 0;
        control.StateChanged += (_, _) => changes++;

        Assert.Equal(OwnerModeState.Running, control.GetState());
        Assert.True((await control.DisableAsync()).IsSuccess);
        Assert.Equal(1, real.DisableCalls);
        real.Raise();
        Assert.Equal(1, changes);

        simulation.OwnerModeState = OwnerModeState.NotInstalled;
        Assert.Equal(2, changes);
        Assert.Equal(OwnerModeState.NotInstalled, control.GetState());
        var enable = await control.EnableAsync();
        Assert.False(enable.IsSuccess);
        Assert.Equal(DebugSimulation.BlockedMessage, enable.ErrorMessage);
        Assert.Equal(0, real.EnableCalls);

        // Any override blocks, not only a service one: a pretend edition must not reach the SCM.
        simulation.Reset();
        simulation.Sku = WindowsSku.Home;
        Assert.Equal(OwnerModeState.Running, control.GetState());
        Assert.False((await control.DisableAsync()).IsSuccess);
        Assert.Equal(1, real.DisableCalls);

        simulation.Reset();
        Assert.True((await control.EnableAsync()).IsSuccess);
        Assert.Equal(1, real.EnableCalls);
    }
}
