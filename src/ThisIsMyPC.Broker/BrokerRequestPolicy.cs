using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Broker;

internal sealed class BrokerRequestPolicy
{
    private const int MaximumOperations = 512;
    private const int MaximumTextLength = 1024;
    private const int MaximumEnforcementTargets = 16;
    private const int OperationsPerConfirmation = 8;

    private static readonly HashSet<string> ChangeModules = new(StringComparer.Ordinal)
    {
        "Explorer",
        "Context Menus",
        "Environment",
        "Startup & Services",
        "Windows Annoyances",
        "Privacy & Telemetry",
        "Windows Update",
        "Power Plans",
    };

    private static readonly HashSet<string> ActionModules = new(StringComparer.Ordinal)
    {
        "Software",
        "Power Plans",
    };

    private readonly IReadOnlyList<ChangeDescriptor> _changes;
    private readonly IReadOnlyList<ActionDescriptor> _actions;
    private readonly string? _restorePointDescription;
    private readonly bool _allowOwnerModeEnable;
    private readonly bool _allowOwnerModeDisable;

    private BrokerRequestPolicy(BrokerSessionRequest request)
    {
        _changes = request.Changes;
        _actions = request.Actions;
        _restorePointDescription = request.RestorePointDescription;
        _allowOwnerModeEnable = request.AllowOwnerModeEnable;
        _allowOwnerModeDisable = request.AllowOwnerModeDisable;
    }

    internal static OperationResult<BrokerRequestPolicy> Create(BrokerSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Changes is null || request.Actions is null
            || request.Changes.Any(change => change is null)
            || request.Actions.Any(action => action is null))
        {
            return Failure("The request contains a null operation collection or entry.");
        }
        var total = request.Changes.Count + request.Actions.Count;
        if (total > MaximumOperations)
            return Failure($"The request contains {total} operations. The limit is {MaximumOperations}.");
        if (total == 0 && request.RestorePointDescription is null
            && !request.AllowOwnerModeEnable && !request.AllowOwnerModeDisable)
        {
            return Failure("The request contains no privileged operation.");
        }

        foreach (var change in request.Changes)
        {
            if (!ChangeModules.Contains(change.ModuleId))
                return Failure($"Module '{change.ModuleId}' cannot use the privilege broker.");
            if (!ValidRequired(change.ModuleId) || !ValidRequired(change.SettingId)
                || !ValidRequired(change.DisplayName) || !ValidRequired(change.SystemLocation)
                || change.BeforeValue is null || !Valid(change.BeforeValue)
                || !Valid(change.AfterValue) || !Valid(change.BeforeDisplay) || !Valid(change.AfterDisplay)
                || !Valid(change.Enforcement))
            {
                return Failure("A change contains invalid or oversized text.");
            }
        }

        foreach (var action in request.Actions)
        {
            if (!ActionModules.Contains(action.ModuleId))
                return Failure($"Module '{action.ModuleId}' cannot execute privileged actions.");
            if (!ValidRequired(action.ModuleId) || !ValidRequired(action.ActionId)
                || !ValidRequired(action.DisplayName) || !ValidRequired(action.Detail)
                || !Valid(action.UndoHint))
            {
                return Failure("An action contains invalid or oversized text.");
            }
        }

        if (!Valid(request.RestorePointDescription)
            || request.RestorePointDescription is not null
            && string.IsNullOrWhiteSpace(request.RestorePointDescription))
            return Failure("The restore point description is invalid or oversized.");

        return OperationResult<BrokerRequestPolicy>.Success(new(request));
    }

    internal bool Authorizes(BrokerCommandRequest command) => command.Kind switch
    {
        BrokerCommandKind.ApplyChange or BrokerCommandKind.RevertChange =>
            command.Change is { } change && _changes.Any(authorized => MatchesEitherDirection(authorized, change)),
        BrokerCommandKind.ExecuteAction =>
            command.Action is { } action && _actions.Any(authorized => Matches(authorized, action)),
        BrokerCommandKind.CreateRestorePoint =>
            _restorePointDescription is not null
            && string.Equals(_restorePointDescription, command.RestorePointDescription, StringComparison.Ordinal),
        BrokerCommandKind.EnableOwnerMode => _allowOwnerModeEnable,
        BrokerCommandKind.DisableOwnerMode => _allowOwnerModeDisable,
        _ => false,
    };

    internal IReadOnlyList<string> BuildConfirmationPages()
    {
        var operations = new List<IReadOnlyList<string>>();
        foreach (var change in _changes)
        {
            var lines = new List<string>
            {
                $"{change.ModuleId}: {change.DisplayName}",
                change.SystemLocation,
                $"New value: {DisplayValue(change.AfterValue)}",
            };
            AddEnforcementTargets(lines, change.Enforcement);
            operations.Add(lines);
        }
        foreach (var action in _actions)
        {
            operations.Add([$"{action.ModuleId}: {action.DisplayName}", action.Detail]);
        }
        if (_restorePointDescription is not null)
            operations.Add([$"Create restore point: {_restorePointDescription}"]);
        if (_allowOwnerModeEnable)
            operations.Add(["Install and start the Owner Mode service."]);
        if (_allowOwnerModeDisable)
            operations.Add(["Pause and stop the Owner Mode service."]);

        var pageCount = (operations.Count + OperationsPerConfirmation - 1) / OperationsPerConfirmation;
        var pages = new List<string>(pageCount);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var lines = new List<string>
            {
                "Approve only if every target below matches your selected changes.",
                $"Page {pageIndex + 1} of {pageCount}",
                string.Empty,
            };
            var start = pageIndex * OperationsPerConfirmation;
            var end = Math.Min(start + OperationsPerConfirmation, operations.Count);
            for (var operationIndex = start; operationIndex < end; operationIndex++)
            {
                lines.Add($"{operationIndex + 1}. {operations[operationIndex][0]}");
                foreach (var detail in operations[operationIndex].Skip(1))
                    lines.Add("   " + detail);
            }
            pages.Add(string.Join(Environment.NewLine, lines));
        }
        return pages;
    }

    private static bool MatchesEitherDirection(ChangeDescriptor authorized, ChangeDescriptor requested)
    {
        if (!string.Equals(authorized.ModuleId, requested.ModuleId, StringComparison.Ordinal)
            || !string.Equals(authorized.SettingId, requested.SettingId, StringComparison.Ordinal)
            || !string.Equals(authorized.SystemLocation, requested.SystemLocation, StringComparison.OrdinalIgnoreCase)
            || authorized.ValueType != requested.ValueType
            || !EnforcementEquals(authorized.Enforcement, requested.Enforcement))
        {
            return false;
        }

        return (string.Equals(authorized.BeforeValue, requested.BeforeValue, StringComparison.Ordinal)
                   && string.Equals(authorized.AfterValue, requested.AfterValue, StringComparison.Ordinal)
               || string.Equals(authorized.BeforeValue, requested.AfterValue, StringComparison.Ordinal)
                   && string.Equals(authorized.AfterValue, requested.BeforeValue, StringComparison.Ordinal));
    }

    private static bool Matches(ActionDescriptor authorized, ActionDescriptor requested) =>
        string.Equals(authorized.ModuleId, requested.ModuleId, StringComparison.Ordinal)
        && string.Equals(authorized.ActionId, requested.ActionId, StringComparison.Ordinal)
        && string.Equals(authorized.Detail, requested.Detail, StringComparison.Ordinal);

    private static bool Valid(string? value) => value is null
        || value.Length <= MaximumTextLength && !value.Any(char.IsControl);

    private static bool ValidRequired(string? value) => !string.IsNullOrWhiteSpace(value) && Valid(value);

    private static bool Valid(Core.Enforcement.SettingEnforcement? enforcement) => enforcement is null
        || Valid(enforcement.CompanionServices)
           && Valid(enforcement.CompanionTasks)
           && Valid(enforcement.GPCacheEntries)
           && Valid(enforcement.ReversionVectors)
           && EnforcementTargetCount(enforcement) <= MaximumEnforcementTargets;

    private static bool Valid(IReadOnlyList<string>? values) => values is null
        || values.Count <= MaximumEnforcementTargets && values.All(Valid);

    private static int EnforcementTargetCount(Core.Enforcement.SettingEnforcement enforcement) =>
        (enforcement.CompanionServices?.Count ?? 0)
        + (enforcement.CompanionTasks?.Count ?? 0)
        + (enforcement.GPCacheEntries?.Count ?? 0)
        + (enforcement.ReversionVectors?.Count ?? 0);

    private static bool EnforcementEquals(
        Core.Enforcement.SettingEnforcement? left,
        Core.Enforcement.SettingEnforcement? right) => left is null
        ? right is null
        : right is not null
          && left.SkuRestriction == right.SkuRestriction
          && left.OwnerModeRequired == right.OwnerModeRequired
          && left.AclElevation == right.AclElevation
          && left.RestoresCompanions == right.RestoresCompanions
          && SequenceEqual(left.CompanionServices, right.CompanionServices)
          && SequenceEqual(left.CompanionTasks, right.CompanionTasks)
          && SequenceEqual(left.GPCacheEntries, right.GPCacheEntries)
          && SequenceEqual(left.ReversionVectors, right.ReversionVectors);

    private static bool SequenceEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        ReferenceEquals(left, right)
        || left is not null && right is not null && left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);

    private static void AddEnforcementTargets(
        List<string> lines,
        Core.Enforcement.SettingEnforcement? enforcement)
    {
        if (enforcement is null)
            return;
        AddTargets(lines, "Service", enforcement.CompanionServices);
        AddTargets(lines, "Task", enforcement.CompanionTasks);
        AddTargets(lines, "Policy cache", enforcement.GPCacheEntries);
        AddTargets(lines, "Reversion vector", enforcement.ReversionVectors);
        if (enforcement.AclElevation)
            lines.Add("ACL ownership change required");
    }

    private static void AddTargets(List<string> lines, string label, IReadOnlyList<string>? values)
    {
        if (values is null)
            return;
        foreach (var value in values)
            lines.Add($"{label}: {value}");
    }

    private static string DisplayValue(string? value) => string.IsNullOrEmpty(value) ? "<absent>" : value;

    private static OperationResult<BrokerRequestPolicy> Failure(string message) =>
        OperationResult<BrokerRequestPolicy>.Failure(message, ErrorCategory.AccessDenied);
}
