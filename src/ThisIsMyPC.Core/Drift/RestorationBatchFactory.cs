using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift;

/// <summary>
/// Turns a validated <see cref="RestorationCandidate"/> plus the value observed
/// at that instant into the same <see cref="ChangeDescriptor"/> and
/// <see cref="ChangeGroup"/> shapes the app stages, so restoration runs through
/// <see cref="IReversibleChangeExecutor"/> and its own
/// <see cref="PendingChangesService"/> with the ordinary before-state and
/// rollback contract. The before value is the typed snapshot, never a
/// fabricated default. Every field that ends up in a descriptor is rechecked
/// against <see cref="RestorationCatalog.Default"/> here; nothing is taken
/// from the candidate on trust. This class builds descriptors only; it does
/// not read, write, or choose a module delegate.
/// </summary>
public static class RestorationBatchFactory
{
    public const string GroupDisplayName = "Owner Mode restoration";

    /// <summary>Builds one descriptor for a candidate whose observed value differs from the desired value.</summary>
    public static RestorationPreparation Prepare(RestorationCandidate candidate, RegistryValueSnapshot observed)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(observed);

        if (CheckCandidate(candidate) is { } untrusted)
            return untrusted;

        var target = candidate.Target;
        if (!observed.IsPresent)
        {
            return RestorationPreparation.Reject(
                RestorationPreparationOutcome.ObservedAbsent,
                $"{target.SettingId} is absent; an absent before-state has no non-sentinel descriptor form yet.");
        }

        var current = observed.Value!;
        if (current.Kind != RegistryValueDataKind.DWord
            || !RegistryValueSnapshot.TryCanonicalize(current, out var canonicalCurrent))
        {
            return RestorationPreparation.Reject(
                RestorationPreparationOutcome.ObservedKindMismatch,
                $"{target.SettingId} holds {current.Kind} data '{current.Data}', not {target.ValueType}.");
        }

        if (observed.Matches(candidate.DesiredValue))
        {
            return RestorationPreparation.Reject(
                RestorationPreparationOutcome.AlreadyMatches,
                $"{target.SettingId} already reads '{candidate.DesiredValue.Data}'.");
        }

        var change = new ChangeDescriptor
        {
            ModuleId = target.ModuleId,
            SettingId = target.SettingId,
            DisplayName = $"Restore after drift: {target.DisplayName}",
            // Derived here from the catalog target and the checked SID, never copied
            // from the candidate: the only key a SYSTEM process can address for a
            // user profile. HKCU is the interactive app's view of the same key.
            SystemLocation = $@"{RestorationCatalog.ResolveUserHivePath(target.KeyPath, candidate.UserSid!)}\{target.ValueName}",
            BeforeValue = canonicalCurrent.Data,
            AfterValue = candidate.DesiredValue.Data,
            BeforeDisplay = "Reverted by Windows",
            AfterDisplay = "Restored",
            ValueType = target.ValueType,
            Category = ChangeCategory.Modify,
            RestartRequirement = RestartRequirement.None,
            // Catalog validation accepts only informational enforcement (reversion
            // vectors), and the candidate carries none of it forward; the descriptor
            // routes straight to the delegate.
            Enforcement = null,
        };

        return RestorationPreparation.Ready(change);
    }

    /// <summary>
    /// One group per restoration pass, so the pending queue rolls the whole pass
    /// back together on a mid-batch failure. Accepts only ready preparations, which
    /// only <see cref="Prepare"/> can issue, and copies their descriptors into a
    /// read-only list so the caller's list cannot alter the group later. Ordinary
    /// groups are still built by hand where the app stages them.
    /// </summary>
    public static ChangeGroup CreateGroup(IReadOnlyList<RestorationPreparation> preparations)
    {
        ArgumentNullException.ThrowIfNull(preparations);
        if (preparations.Count == 0)
            throw new ArgumentException("A restoration group needs at least one prepared change.", nameof(preparations));

        var changes = new List<ChangeDescriptor>(preparations.Count);
        foreach (var preparation in preparations)
        {
            ArgumentNullException.ThrowIfNull(preparation, nameof(preparations));
            if (!preparation.IsReady)
            {
                throw new ArgumentException(
                    $"Only ready preparations belong in a restoration group; got {preparation.Outcome}.",
                    nameof(preparations));
            }

            changes.Add(preparation.Change!);
        }

        return new ChangeGroup
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = GroupDisplayName,
            Description = changes.Count == 1
                ? "Put one drifted value back to the state ThisIsMyPC applied."
                : $"Put {changes.Count} drifted values back to the state ThisIsMyPC applied.",
            Changes = changes.AsReadOnly(),
        };
    }

    /// <summary>
    /// Rechecks every candidate field against the shipped catalog. The target must
    /// be the catalog's own instance (a structural copy from another catalog or a
    /// <c>with</c> expression fails), the desired value must be canonical and
    /// listed for that target, the SID must be an account SID, and the resolved
    /// path must be the one derived from the target and SID.
    /// </summary>
    private static RestorationPreparation? CheckCandidate(RestorationCandidate candidate)
    {
        var target = candidate.Target;
        if (target is null)
            return Untrusted("candidate has no target.");

        if (string.IsNullOrWhiteSpace(target.KeyPath) || string.IsNullOrWhiteSpace(target.ValueName))
            return Untrusted($"target {target.ModuleId}/{target.SettingId} has no key path or value name.");

        var known = RestorationCatalog.Default.FindByLocation(target.KeyPath, target.ValueName);
        if (known is null || !ReferenceEquals(known, target))
            return Untrusted($"target {target.ModuleId}/{target.SettingId} at '{target.KeyPath}\\{target.ValueName}' is not the shipped catalog entry.");

        if (target.ValueType != ChangeValueType.Registry_DWord || !target.IsUserHive)
            return Untrusted($"{target.SettingId} is not a user-hive DWORD target.");

        var desired = candidate.DesiredValue;
        if (desired is null
            || desired.Kind != RegistryValueDataKind.DWord
            || !RegistryValueSnapshot.TryCanonicalize(desired, out var canonicalDesired)
            || canonicalDesired != desired
            || !target.AllowedDesiredValues.Contains(desired))
        {
            return Untrusted($"desired value '{desired?.Data}' is not an allowed canonical value for {target.SettingId}.");
        }

        if (string.IsNullOrEmpty(candidate.UserSid) || !RestorationCatalog.IsAccountSid(candidate.UserSid))
            return Untrusted($"'{candidate.UserSid}' is not a user account SID.");

        var expectedPath = RestorationCatalog.ResolveUserHivePath(target.KeyPath, candidate.UserSid);
        if (!string.Equals(candidate.ResolvedKeyPath, expectedPath, StringComparison.Ordinal))
            return Untrusted($"resolved path '{candidate.ResolvedKeyPath}' is not '{expectedPath}'.");

        return null;
    }

    private static RestorationPreparation Untrusted(string detail)
        => RestorationPreparation.Reject(RestorationPreparationOutcome.UntrustedCandidate, $"Restoration refused: {detail}");
}
