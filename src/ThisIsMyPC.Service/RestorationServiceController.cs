using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Service;

/// <summary>Explicitly supplied trusted restoration graph. Production leaves this graph absent.</summary>
public sealed class RestorationServiceController(
    RestorationLoop loop, MutationCoordinator coordinator, IMutationLeaseProvider leases,
    IMachineConsentStore consent, Func<IMutationLease, OperationResult<bool>> readiness,
    TimeProvider time) : IRestorationServiceController
{
    private RestorationStatusResponse _status = new();

    public RestorationStatusResponse GetStatus()
    {
        var current = consent.Read();
        var status = Volatile.Read(ref _status);
        if (current.Status != MachineConsentStatus.Loaded)
            return status with { State = RestorationServiceState.Unavailable, ConsentGranted = false, Detail = current.Detail };
        if (!current.IsGranted)
            return status with { State = RestorationServiceState.Paused, ConsentGranted = false, Detail = "Restoration is paused." };
        return status.State == RestorationServiceState.Paused
            ? status with { State = RestorationServiceState.Unavailable, ConsentGranted = true, Detail = "Waiting for a fresh restoration scan." }
            : status with { ConsentGranted = true };
    }

    public async Task<RestorationStatusResponse> EnableAsync(CancellationToken token = default)
    {
        Volatile.Write(ref _status, new() { Detail = "Checking trusted restoration evidence." });
        var run = await coordinator.RunAsync(TimeSpan.FromSeconds(5), (lease, _) =>
        {
            var ready = readiness(lease);
            if (!ready.IsSuccess || !ready.Value)
                return Task.FromResult(new RestorationStatusResponse { Detail = ready.ErrorMessage ?? "Trusted restoration evidence is unavailable." });
            var saved = consent.SetEnabled(true, lease);
            return Task.FromResult(saved.IsSuccess && saved.State.IsGranted
                ? new RestorationStatusResponse { State = RestorationServiceState.Enabled, ConsentGranted = true, Detail = "Restoration is enabled." }
                : new RestorationStatusResponse { Detail = saved.State.Detail });
        }, token).ConfigureAwait(false);
        var status = run.OperationRan ? run.Value! : new RestorationStatusResponse
        { State = RestorationServiceState.Conflict, Detail = "Recovery or another operation prevents enabling restoration." };
        Volatile.Write(ref _status, status);
        return status;
    }

    public async Task<RestorationStatusResponse> PauseAsync(CancellationToken token = default)
    {
        Volatile.Write(ref _status, new() { Detail = "Waiting for durable pause confirmation." });
        var acquired = await leases.AcquireAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
        if (!acquired.IsAcquired)
            return new() { State = RestorationServiceState.Conflict, Detail = "An active operation prevents pause." };
        await using var lease = acquired.Lease!;
        token.ThrowIfCancellationRequested();
        var saved = consent.SetEnabled(false, lease);
        var status = saved.IsSuccess && saved.State.Status == MachineConsentStatus.Loaded && !saved.State.Enabled
            ? new RestorationStatusResponse { State = RestorationServiceState.Paused, Detail = "Restoration is paused." }
            : new RestorationStatusResponse { Detail = saved.State.Detail };
        Volatile.Write(ref _status, status);
        return status;
    }

    /// <summary>Tests trigger scans directly; hosted scans use the existing bounded cadence.</summary>
    public async Task RunAsync(Func<CancellationToken, Task<bool>>? nextTick = null, CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            await ScanAsync(token).ConfigureAwait(false);
            if (nextTick is not null)
            {
                if (!await nextTick(token).ConfigureAwait(false)) return;
            }
            else await Task.Delay(RestorationLoop.DefaultInterval, time, token).ConfigureAwait(false);
        }
    }

    /// <summary>Runs one real coordinated restoration attempt and publishes its result.</summary>
    public async Task ScanAsync(CancellationToken token = default)
    {
        try
        {
            var result = await loop.ScanAsync(token).ConfigureAwait(false);
            var state = result.Status switch
            {
                RestorationScanStatus.Restored or RestorationScanStatus.AlreadyMatches => RestorationServiceState.Enabled,
                RestorationScanStatus.ConsentOff => RestorationServiceState.Paused,
                RestorationScanStatus.RecoveryRequired or RestorationScanStatus.CoordinationBlocked or
                    RestorationScanStatus.Uncertain or RestorationScanStatus.AttemptLimitReached => RestorationServiceState.Conflict,
                _ => RestorationServiceState.Unavailable,
            };
            Volatile.Write(ref _status, new() { State = state, ConsentGranted = consent.Read().IsGranted,
                Detail = result.Detail, LastScanUtc = time.GetUtcNow() });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Volatile.Write(ref _status, new() { State = RestorationServiceState.Unavailable,
                Detail = "Restoration scan failed: " + ex.Message, LastScanUtc = time.GetUtcNow() });
        }
    }
}

public interface IRestorationServiceController
{
    RestorationStatusResponse GetStatus();
    Task<RestorationStatusResponse> EnableAsync(CancellationToken token = default);
    Task<RestorationStatusResponse> PauseAsync(CancellationToken token = default);
    Task<IReadOnlyList<ThisIsMyPC.Core.Changes.ChangeHistoryEntry>> GetHistoryAsync(CancellationToken token = default) =>
        Task.FromResult<IReadOnlyList<ThisIsMyPC.Core.Changes.ChangeHistoryEntry>>([]);
}
