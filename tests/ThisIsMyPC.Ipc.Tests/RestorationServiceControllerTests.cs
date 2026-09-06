using System.Text.Json;
using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Ipc.Contracts;
using ThisIsMyPC.Service;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class RestorationServiceControllerTests
{
    [Fact]
    public async Task EnableScanStatusPauseUsesRealLoopAndDurableHistory()
    {
        using var fixture = new RestorationLoopFixture();
        await fixture.InitializeAsync();
        fixture.Consent.Enabled = false;
        var controller = Create(fixture);
        var handler = new IpcRequestHandler(new Drift(), DateTimeOffset.UtcNow, "test", controller);
        var enabled = await handler.HandleAsync(new() { Type = IpcMessageTypes.EnableRestoration, Nonce = "enable" });
        Assert.Equal("enable", enabled.Nonce);
        Assert.Equal(RestorationServiceState.Enabled, Decode(enabled).State);
        var ticks = 0;
        await controller.RunAsync(_ => Task.FromResult(++ticks < 2));
        Assert.Equal(2, ticks);
        Assert.Single(await fixture.Repository.GetAllAsync());
        Assert.Equal(1, fixture.Registry.Writes);
        var status = handler.Handle(new() { Type = IpcMessageTypes.ServiceStatus, Nonce = "status" });
        var payload = JsonSerializer.Deserialize(status.PayloadJson!, IpcJsonContext.Default.ServiceStatusResponse)!;
        Assert.True(payload.Restoration.ConsentGranted);
        Assert.Equal(RestorationServiceState.Enabled, payload.Restoration.State);
        Assert.NotNull(payload.Restoration.LastScanUtc);
        var paused = await handler.HandleAsync(new() { Type = IpcMessageTypes.PauseRestoration, Nonce = "pause" });
        Assert.Equal(RestorationServiceState.Paused, Decode(paused).State);
        Assert.False(fixture.Consent.Enabled);
        fixture.Registry.Value = fixture.Target.WindowsDefaultValue;
        await controller.ScanAsync();
        Assert.Equal(1, fixture.Registry.Writes);
        Assert.Equal(RestorationServiceState.Paused, controller.GetStatus().State);
    }

    [Fact]
    public async Task MissingReadinessNeverGrantsConsent()
    {
        using var fixture = new RestorationLoopFixture();
        await fixture.InitializeAsync();
        fixture.Consent.Enabled = false;
        var controller = Create(fixture, ready: false);
        Assert.Equal(RestorationServiceState.Unavailable, (await controller.EnableAsync()).State);
        Assert.False(fixture.Consent.Enabled);
        await controller.ScanAsync();
        Assert.Equal(0, fixture.Registry.Writes);
    }

    [Fact]
    public async Task PauseDoesNotRequireRecoveryAndExternalConsentDoesNotReusePausedStatus()
    {
        using var fixture = new RestorationLoopFixture();
        await fixture.InitializeAsync();
        var controller = new RestorationServiceController(fixture.Loop,
            new(fixture.Provider, (_, _) => Task.FromResult(OperationResult<bool>.Failure("corrupt", ErrorCategory.ServiceUnavailable))),
            fixture.Provider, fixture.Consent, _ => OperationResult<bool>.Success(true), fixture.Journal.TimeProvider);
        Assert.Equal(RestorationServiceState.Conflict, (await controller.EnableAsync()).State);
        Assert.Equal(RestorationServiceState.Paused, (await controller.PauseAsync()).State);
        fixture.Consent.Enabled = true;
        Assert.Equal(RestorationServiceState.Unavailable, controller.GetStatus().State);
    }

    private static RestorationServiceController Create(RestorationLoopFixture fixture, bool ready = true) => new(
        fixture.Loop, new MutationCoordinator(fixture.Provider, (_, _) => Task.FromResult(OperationResult<bool>.Success(true))),
        fixture.Provider, fixture.Consent, _ => OperationResult<bool>.Success(ready), fixture.Journal.TimeProvider);

    private static RestorationStatusResponse Decode(IpcEnvelope envelope) =>
        JsonSerializer.Deserialize(envelope.PayloadJson!, IpcJsonContext.Default.RestorationStatusResponse)!;

    private sealed class Drift : IDriftReportSource
    {
        public bool BaselinePresent => true;
        public DateTimeOffset? LastScanUtc => null;
        public DriftReportResponse GetReport() => new() { BaselinePresent = true };
    }
}
