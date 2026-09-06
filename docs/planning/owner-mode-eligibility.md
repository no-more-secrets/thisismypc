# Owner Mode eligibility foundation

The pure `RestorationEligibilityPolicy` evaluates one baseline entry using supplied evidence.
It performs no probing, writes, persistence, journal parsing, or service activation.
`TimeProvider` supplies the evaluation time.

## Gates

1. Machine restoration consent must explicitly be true. Its default is false.
2. `RestorationCatalog.Default.Validate` must accept the exact entry and its user SID.
3. Profile evidence must name that SID and report a supported, loaded profile.
4. Management evidence must identify that exact catalog target and SID as unmanaged.
5. Retry history must be complete and valid for the decision.
6. Fewer than three qualifying attempts may remain in the seven-day window after reset.

A valid SID does not prove profile support or hive availability.
Unknown, unsupported, unloaded, or mismatched profile evidence blocks restoration.
Unknown management blocks every target, including registry preferences outside Policies paths.
Management evidence for another target or user does not apply.
Unknown enum values fail closed.

Decisions are immutable records with a reason, evaluation time, and counted attempts.
Catalog rejection preserves its validation outcome and detail.
The accepted candidate uses the existing catalog type and canonical location.
A decision is a snapshot, not a durable authorization.
The future caller must recheck consent and evidence under the mutation lock before writing.

## Retry input

`RestorationRetryObservation` is a small projection, not a journal model.
A future adapter supplies one observation for each failed or repeatedly reverted attempt.
Success without a subsequent qualifying reversion does not produce an observation.
Uncertain execution must remain blocked by journal reconciliation; this policy does not resolve it.

The adapter must preserve the attempt ID and one stable timestamp for each qualifying attempt.
Repeated journal imports must retain those values.
Identical observations for one attempt count once.
Conflicting timestamps for one attempt fail closed.
Empty attempt IDs and future timestamps fail closed for the matching target and SID.
The adapter must not mark incomplete, unreadable, or untrusted history as complete.

Identity uses the catalog module, setting, key, value name, and user SID.
Use `RestorationIdentity.FromCandidate` to obtain its canonical form.
Evidence for another target or user neither consumes nor resets this budget.
Comparisons use exact identity, including the canonical key spelling.

The window includes attempts exactly seven days before the injected current time.
An attempt one tick older does not count.
Two qualifying attempts allow one further attempt; three block further attempts.
An explicit reset excludes observations at or before its timestamp for that identity.
It does not grant consent, establish profile support, or override management exclusion.
A future reset for the matching identity fails closed.
The caller must journal resets durably before supplying them here.

## Verification

Eighteen tests cover every shipped target, default consent, unsupported and unloaded profiles,
unknown management, mixed users and targets, retry boundaries, duplicate observations, resets, and future dates.
They also verify catalog rejection and aging through the injected clock.

Run the focused suite:

```powershell
dotnet test tests/ThisIsMyPC.Core.Tests --configuration Release --filter "FullyQualifiedName~Drift.Eligibility"
```

No live profile inventory, management detection, journal adapter, consent storage, or service worker is implemented here.