using ThisIsMyPC.Core.Coordination;
using ThisIsMyPC.Core.Drift.Baseline;
using ThisIsMyPC.Core.Drift.Consent;
using ThisIsMyPC.Core.Drift.Journal;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift;

/// <summary>
/// Shared recovery for deliberate changes and restoration. Recovery never writes a preference.
/// Uncertain attempts disable consent and remain diagnostic. Readiness must still refuse them.
/// </summary>
public sealed class RestorationRecovery(SingleOwnerBaselineStore baseline, RestorationJournal journal,
    IMachineConsentStore consent, IRegistryService registry, string leaseName)
{
    public Task<OperationResult<bool>> RecoverAsync(IMutationLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        if (!lease.IsHeld || lease.CanWrite || !string.Equals(lease.Name, leaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("Recovery requires the matching unrecovered machine lease.");
        try
        {
            _ = baseline.Read(lease);
            var snapshot = journal.Read(lease);
            if (!snapshot.IsHealthy)
                throw new InvalidDataException("Restoration journal is corrupt.");
            // Validate every identity before recording any observation.
            foreach (var attempt in snapshot.Attempts)
                _ = Candidate(attempt.Intent);
            if (snapshot.Attempts.Any(a => a.Outcome is null || a.DiagnosticOnly ||
                    a.Outcome.Kind is JournalOutcomeKind.Uncertain or JournalOutcomeKind.RecoveryObservation))
            {
                Disable(lease);
                foreach (var attempt in snapshot.Attempts.Where(a => a.Outcome is null))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var candidate = Candidate(attempt.Intent);
                    var read = registry.ReadValue(candidate.ResolvedKeyPath, candidate.ValueName);
                    var observed = read.IsSuccess && read.Value is not null
                        ? RegistryValueSnapshot.Present(read.Value)
                        : read.ErrorCategory == ErrorCategory.NotFound
                            ? RegistryValueSnapshot.Absent
                            : throw new IOException("Interrupted restoration could not be observed: " + read.ErrorMessage);
                    journal.RecordRecoveryObservation(attempt.Intent.AttemptId, observed,
                        "Observed after interruption. This does not prove that restoration completed.", lease);
                }
            }
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Inhibit automatic writes even when evidence cannot be reconciled.
            // Failure stays visible and does not mark the lease recovered.
            try { Disable(lease); }
            catch (Exception offFailure)
            {
                return Task.FromResult(OperationResult<bool>.Failure(
                    "Recovery failed and consent-off could not be confirmed: " + offFailure.Message,
                    ErrorCategory.ServiceUnavailable));
            }
            return Task.FromResult(OperationResult<bool>.Failure("Owner Mode recovery failed: " + ex.Message,
                ErrorCategory.ServiceUnavailable));
        }
    }

    private void Disable(IMutationLease lease)
    {
        var off = consent.SetEnabled(false, lease);
        if (!off.IsSuccess || off.State.Status != MachineConsentStatus.Loaded || off.State.Enabled)
            throw new IOException("Consent-off could not be confirmed: " + off.State.Detail);
    }

    private RestorationCandidate Candidate(JournalIntent intent)
    {
        if (intent.UserSid != baseline.PrimaryUserSid)
            throw new InvalidDataException("Journal attempt belongs to another owner.");
        var validation = RestorationCatalog.Default.Validate(new DriftBaselineEntry
        {
            ModuleId = intent.ModuleId, SettingId = intent.SettingId, DisplayName = intent.SettingId,
            SystemLocation = intent.CanonicalLocation, ExpectedValue = intent.Desired.Data,
            ValueType = Changes.ChangeValueType.Registry_DWord, UpdatedAtUtc = intent.CreatedAt,
        }, intent.UserSid);
        if (!validation.IsAccepted || validation.Candidate!.DesiredValue != intent.Desired)
            throw new InvalidDataException("Journal attempt is outside the restoration catalog.");
        return validation.Candidate;
    }
}
