using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Sets;

/// <summary>
/// Resolves every entry of a set against the current system state (via the module
/// inspectors) and the already-pending changes, producing the preview/staging decisions
/// of Story 8.3. Pure logic — all system access goes through the injected inspectors.
/// </summary>
public sealed class SetConflictResolver
{
    private readonly IReadOnlyList<ISetEntryInspector> _inspectors;
    private readonly Func<string, ModuleAvailability?> _moduleAvailabilityLookup;

    public SetConflictResolver(
        IEnumerable<ISetEntryInspector> inspectors,
        Func<string, ModuleAvailability?> moduleAvailabilityLookup)
    {
        _inspectors = inspectors.ToList();
        _moduleAvailabilityLookup = moduleAvailabilityLookup;
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
                $"Will be skipped — the '{entry.ModuleId}' module is not part of this build.");
        }

        if (!availability.IsAvailable)
        {
            return Skipped(entry,
                $"Will be skipped — {availability.Reason ?? $"the '{entry.ModuleId}' module is not available on this system"}.");
        }

        var inspector = _inspectors.FirstOrDefault(i => i.ModuleId == entry.ModuleId);
        var state = inspector?.Inspect(entry);
        if (inspector is null || state is null)
        {
            return Skipped(entry,
                "Will be skipped — this setting is not recognized by the installed version.");
        }

        // A resolvable setting can still carry an unstageable value (hand-edited user
        // sets); validate the stage path now so the row never dangles a dead checkbox.
        if (inspector.CreateChangeGroup(entry) is null)
        {
            return Skipped(entry,
                $"Will be skipped — the value '{entry.Value}' is not valid for this setting.");
        }

        // First pending descriptor targeting the same setting. Factories list a group
        // toggle's primary value first, so the first match compares against the same
        // primary the entry's Value uses.
        foreach (var group in pendingGroups)
        {
            foreach (var change in group.Changes)
            {
                if (change.ModuleId != entry.ModuleId || change.SettingId != entry.SettingId)
                    continue;

                var sameValue = string.Equals(change.AfterValue, entry.Value, StringComparison.Ordinal);
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
                };
            }
        }

        return new SetEntryResolution
        {
            Entry = entry,
            State = state,
            SkipReason = null,
            Conflict = state.IsApplied ? SetEntryConflict.AlreadyApplied : SetEntryConflict.None,
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
