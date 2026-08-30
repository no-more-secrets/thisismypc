using System.ComponentModel;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>
/// Staging queue for one-way actions, parallel to <see cref="IPendingChangesService"/>.
/// Same stage/review/apply rhythm, but no rollback and no history: succeeded actions
/// leave the queue, failed ones stay staged so the user can retry or discard them.
/// </summary>
public interface IPendingActionsService : INotifyPropertyChanged
{
    int PendingCount { get; }
    IReadOnlyList<ActionDescriptor> PendingActions { get; }
    bool IsApplying { get; }

    /// <summary>Display name of the action currently executing, or null when idle.</summary>
    string? CurrentActionDisplay { get; }

    void Stage(ActionDescriptor action);
    void Unstage(string actionId);
    bool IsStaged(string actionId);
    void DiscardAll();

    Task<ActionBatchResult> ApplyAllAsync(
        Func<ActionDescriptor, Task<OperationResult<bool>>> executeFunc);
}
