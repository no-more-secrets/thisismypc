using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// Asks Windows to re-read Group Policy now. Services that cache policy
/// state (the power service keeps "the active plan is pinned" until policy
/// is re-applied) pick up a registry change only after this runs.
/// </summary>
public interface IPolicyRefreshService
{
    /// <summary>Machine policy, forced: every client-side extension runs again even when nothing changed.</summary>
    OperationResult<bool> RefreshMachinePolicy();
}
