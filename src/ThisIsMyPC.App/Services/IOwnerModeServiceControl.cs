using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// The Owner Mode service lifecycle as the Settings page drives it: read the
/// SCM state, enable, disable. OwnerModeService is the real one; tests render
/// the Settings card against a fake without touching the SCM.
/// </summary>
public interface IOwnerModeServiceControl : IOwnerModeLifecycle
{
    OwnerModeState GetState();
    Task<RestorationStatusResponse> GetRestorationStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new RestorationStatusResponse());

    Task<OperationResult<bool>> DisableAsync(CancellationToken cancellationToken = default);
}
