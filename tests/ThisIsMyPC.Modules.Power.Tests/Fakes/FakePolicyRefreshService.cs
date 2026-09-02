using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Power.Tests.Fakes;

/// <summary>Counts refreshes; a callback lets a test flip the power fake's behaviour when policy is re-read.</summary>
public sealed class FakePolicyRefreshService : IPolicyRefreshService
{
    public int Refreshes { get; private set; }
    public Action? OnRefresh { get; set; }

    public OperationResult<bool> RefreshMachinePolicy()
    {
        Refreshes++;
        OnRefresh?.Invoke();
        return OperationResult<bool>.Success(true);
    }
}
