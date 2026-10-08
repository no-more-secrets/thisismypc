using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Sets;

/// <summary>
/// Resolves every entry of a set against the current system state (via the module
/// inspectors) and the already-pending changes, producing the preview/staging decisions
/// of Story 8.3. Pure logic; all system access goes through the injected inspectors.
/// </summary>
public sealed class SetConflictResolver
{
    private readonly IReadOnlyList<ISetEntryInspector> _inspectors;
    private readonly Func<string, ModuleAvailability?> _moduleAvailabilityLookup;
    private readonly ICapabilityDetector? _capabilityDetector;
    private readonly Policies.PolicyControlStateReader? _policyStates;

    public SetConflictResolver(
        IEnumerable<ISetEntryInspector> inspectors,
        Func<string, ModuleAvailability?> moduleAvailabilityLookup,
        ICapabilityDetector? capabilityDetector = null,
        Policies.PolicyControlStateReader? policyStates = null)
    {
        _inspectors = inspectors.ToList();
        _moduleAvailabilityLookup = moduleAvailabilityLookup;
        _capabilityDetector = capabilityDetector;
        _policyStates = policyStates;
    }

    public IReadOnlyList<SetEntryResolution> Resolve(
        SetDefinition definition, IReadOnlyList<ChangeGroup> pendingGroups)
        => definition.Entries.Select(entry => ResolveEntry(entry, pendingGroups)).ToList();

    private SetEntryResolution ResolveEntry(SetEntry entry, IReadOnlyList<ChangeGroup> pendingGroups)
    {
        var availability = _moduleAvailabilityLookup(entry.ModuleId);
        if (availability is null)
        {
            return Skipped(entry,
                $"Will be skipped: the '{entry.ModuleId}' module is not part of this build.");
        }

        if (!availability.IsAvailable)
        {
            return Skipped(entry,
                $"Will be skipped: {availability.Reason ?? $"the '{entry.ModuleId}' module is not available on this system"}.");
        }

        var inspector = _inspectors.FirstOrDefault(i => i.ModuleId == entry.ModuleId);
        var state = inspector?.Inspect(entry);
        if (inspector is null || state is null)
        {
            return Skipped(entry,
                "Will be skipped: this setting is not recognized by the installed version.");
        }

        // A resolvable setting can still carry an unstageable value (hand-edited user
        // sets); validate the stage path now so the row never dangles a dead checkbox.
        var stageable = inspector.CreateChangeGroup(entry);
        if (stageable is null)
        {
            return Skipped(entry,
                $"Will be skipped: the value '{entry.Value}' is not valid for this setting.");
        }

        var skuNotice = stageable.Changes
            .Select(change => SettingEditionSupport.BlockReason(_capabilityDetector?.Sku, change))
            .FirstOrDefault(reason => reason is not null);
        if (skuNotice is not null)
            return Skipped(entry, skuNotice) with { State = state, SkuNotice = skuNotice };

        var policy = stageable.Changes.Select(change => _policyStates?.Read(change))
            .FirstOrDefault(state => state?.BlocksChanges == true);
        if (policy is not null)
            return Skipped(entry, policy.Message!) with { State = state with { CurrentDisplay = policy.Message!, CoveredByPolicy = policy.Message } };

        // Compare every resolved write. Paired policies can share a primary value
        // while selecting different behavior through their companion value.
        foreach (var group in pendingGroups)
        {
            foreach (var change in group.Changes)
            {
                if (change.ModuleId != entry.ModuleId || change.SettingId != entry.SettingId)
                    continue;

                var sameValue = stageable.Changes.All(expected => group.Changes.Any(queued =>
                        queued.ModuleId == expected.ModuleId && queued.SettingId == expected.SettingId &&
                        queued.SystemLocation.Equals(expected.SystemLocation, StringComparison.OrdinalIgnoreCase) &&
                        queued.ValueType == expected.ValueType && queued.AfterValue == expected.AfterValue));
                return new SetEntryResolution
                {
                    Entry = entry,
                    State = state,
                    SkipReason = null,
                    Conflict = sameValue
                        ? SetEntryConflict.PendingSameValue
                        : SetEntryConflict.PendingDifferentValue,
                    PendingGroupId = group.GroupId,
                    PendingValue = change.AfterValue,
                    PendingDisplay = change.AfterDisplay,
                    SkuNotice = skuNotice,
                };
            }
        }

        return new SetEntryResolution
        {
            Entry = entry,
            State = state,
            SkipReason = null,
            Conflict = state.IsApplied ? SetEntryConflict.AlreadyApplied : SetEntryConflict.None,
            SkuNotice = skuNotice,
        };
    }

    private static SetEntryResolution Skipped(SetEntry entry, string reason) => new()
    {
        Entry = entry,
        State = null,
        SkipReason = reason,
        Conflict = SetEntryConflict.None,
    };
}
