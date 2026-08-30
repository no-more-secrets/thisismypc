using System.ComponentModel;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public sealed class PendingActionsService : IPendingActionsService
{
    private readonly List<ActionDescriptor> _pendingActions = [];
    private readonly object _lock = new();

    public int PendingCount
    {
        get { lock (_lock) return _pendingActions.Count; }
    }

    public IReadOnlyList<ActionDescriptor> PendingActions
    {
        get { lock (_lock) return _pendingActions.ToList().AsReadOnly(); }
    }

    public bool IsApplying { get; private set; }

    public string? CurrentActionDisplay { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Stage(ActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_lock)
        {
            // Idempotent by ActionId — a checkbox re-check must not duplicate work.
            if (_pendingActions.Any(a => a.ActionId == action.ActionId))
                return;

            _pendingActions.Add(action);
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingActions));
    }

    public void Unstage(string actionId)
    {
        ArgumentNullException.ThrowIfNull(actionId);

        bool removed;
        lock (_lock)
        {
            var index = _pendingActions.FindIndex(a => a.ActionId == actionId);
            removed = index >= 0;
            if (removed)
                _pendingActions.RemoveAt(index);
        }

        if (removed)
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(PendingActions));
        }
    }

    public bool IsStaged(string actionId)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        lock (_lock) return _pendingActions.Any(a => a.ActionId == actionId);
    }

    public void DiscardAll()
    {
        lock (_lock)
        {
            if (_pendingActions.Count == 0)
                return;

            _pendingActions.Clear();
        }

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingActions));
    }

    /// <remarks>
    /// PropertyChanged notifications after awaits may fire on a thread pool thread
    /// (ConfigureAwait(false)); UI-bound consumers must marshal.
    /// </remarks>
    public async Task<ActionBatchResult> ApplyAllAsync(
        Func<ActionDescriptor, Task<OperationResult<bool>>> executeFunc)
    {
        ArgumentNullException.ThrowIfNull(executeFunc);

        List<ActionDescriptor> snapshot;
        lock (_lock)
        {
            snapshot = [.. _pendingActions];
        }

        IsApplying = true;
        OnPropertyChanged(nameof(IsApplying));

        var succeeded = new List<ActionDescriptor>();
        var failed = new List<ActionFailure>();

        try
        {
            foreach (var action in snapshot)
            {
                CurrentActionDisplay = action.DisplayName;
                OnPropertyChanged(nameof(CurrentActionDisplay));

                OperationResult<bool> result;
                try
                {
                    result = await executeFunc(action).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A crashed executor must not abandon the rest of an independent batch
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    result = OperationResult<bool>.Failure(ex.Message, ErrorCategory.ServiceUnavailable, ex);
                }

                if (result.IsSuccess)
                    succeeded.Add(action);
                else
                    failed.Add(new ActionFailure(action, result.ErrorMessage, result.ErrorCategory));
            }

            // Succeeded actions leave the queue; failed ones stay staged for retry.
            // Removal is by ActionId so items staged mid-batch survive untouched.
            var succeededIds = succeeded.Select(a => a.ActionId).ToHashSet();
            lock (_lock)
            {
                _pendingActions.RemoveAll(a => succeededIds.Contains(a.ActionId));
            }

            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(PendingActions));

            return new ActionBatchResult
            {
                Succeeded = succeeded.AsReadOnly(),
                Failed = failed.AsReadOnly(),
            };
        }
        finally
        {
            IsApplying = false;
            CurrentActionDisplay = null;
            OnPropertyChanged(nameof(IsApplying));
            OnPropertyChanged(nameof(CurrentActionDisplay));
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
