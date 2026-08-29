namespace ThisIsMyPC.Core.Services;

/// <summary>
/// Creates Windows System Restore points. Safety-net operation — not a setting
/// mutation, so it does not flow through the pending-changes pipeline.
/// </summary>
public interface IRestorePointService
{
    Task<RestorePointResult> CreateRestorePointAsync(string description);
}
