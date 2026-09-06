# Owner Mode restoration plan

Owner Mode today detects drift after boot and reports it over IPC; the app
reapplies through the normal pending-changes pipeline when the person asks.
Restoration means the service puts a drifted value back on its own. This
document records what shipped as the first foundation batch (2026-09-06),
the rules the review fixed, and the remaining work in dependency order.
Restoration stays off by default until every step below is done and reviewed.

Source of the rules: `artifacts/diagnostics/v1-planning/owner-mode-review.md`
(local, not committed). The rules are restated here so the repo carries them.

## Shipped: typed snapshots and the exact-target catalog

All in `src/ThisIsMyPC.Core/Drift`, tests in `tests/ThisIsMyPC.Core.Tests/Drift`.
Pure contracts only: no service change, no registry write, no wiring.

- `RegistryValueSnapshot`: a value is Absent or Present with typed
  `RegistryValueData`. A present empty string and a present DWORD 0 are both
  present and never equal Absent. `FromRead` maps only a NotFound failure to
  Absent; every other failure passes through, so a failed probe is never
  reported as a missing value. `Matches` requires the same kind and the same
  canonical data (numbers re-rendered as invariant decimal, binary by bytes,
  strings exact). `TryCanonicalize` is shared with the catalog.
- `RestorationTarget`: exact module id, setting id, HKCU key path, value name,
  `Registry_DWord`, the suppressed and Windows-default values, the closed
  `AllowedDesiredValues` list, and a provenance line naming the reader and
  factory the values were copied from.
- `RestorationCatalog`: immutable (`ImmutableArray`, `ImmutableDictionary`),
  one shared `Default`. The constructor refuses non-HKCU roots, any
  `\Policies\` key, empty segments, `(Default)` value names, non-DWORD types,
  empty, duplicate, or non-canonical allowed values, and duplicate locations.
  `Validate(entry, userSid)` gates one `DriftBaselineEntry`: malformed
  location, unknown location, wrong module or setting id for a known
  location, value type mismatch, malformed value (including the `__absent__`
  sentinel), value outside the allowed list, enforcement that does not parse,
  enforcement the batch cannot honor, missing user SID, malformed or
  service-account SID. An accepted entry becomes a `RestorationCandidate`
  with the target, the canonical desired value, the profile SID, and the
  `HKU\{sid}\...` path a SYSTEM process would use.

Enforcement rule: only reversion vectors are compatible, because they are
informational. Companion services or tasks, GPCache entries, ACL elevation,
companion restoration, the Owner-Mode-required flag, and any edition tier
claim are refused. None of the catalog targets are policies, and no tier
support has been verified for them, so a tier claim in a baseline row is a
mismatch, not a feature.

SID rule: only two shapes name a user profile and only those are accepted.
A local or domain account is `S-1-5-21-{d1}-{d2}-{d3}-{rid}`; a Microsoft
Entra account is `S-1-12-1-{a}-{b}-{c}-{d}`. Every number must be canonical
decimal within its MS-DTYP 2.4.2 width: revision 1, identifier authority
under 2^48, one to fifteen 32-bit sub-authorities (both shapes have exactly
five). Leading zeros, signs, non-ASCII digits, and surrounding whitespace
fail. Well-known domain group RIDs from MS-DTYP 2.4.2.4 (498, 512 to 522,
525 to 527, 553, 571, 572) fail because they are groups, not accounts.
Service, builtin, capability, and logon-session principals fail the shape
test. User-created groups (RID 1000 and up) are not distinguishable by shape
and are excluded later by profile enumeration. The check is pure string
work; Core makes no Win32 lookups. The candidate keeps the SID so a later
machine-scoped pass restores each profile's own values. There is no
single-consenting-SID filter, by design.

Canonicalization rule: `TryCanonicalize` recognizes the six
`RegistryValueDataKind` members explicitly. Null data, an unrecognized kind,
or data invalid for its kind returns false and never throws. Empty text is
valid present data for String, ExpandString, and MultiString.

### Catalog contents

Eleven Windows Annoyances singles, all HKCU DWORD toggles staged through
`AnnoyanceChangeFactory.CreateToggle` (or `CreateDriftFragileToggle` for
`dynamic-search-box`), whose restore direction writes the default value
explicitly and never deletes:

| Setting id | Key | Value | Suppressed | Default |
| --- | --- | --- | --- | --- |
| scoobe-nags | `...\CurrentVersion\UserProfileEngagement` | ScoobeSystemSettingEnabled | 0 | 1 |
| welcome-experience | `...\CurrentVersion\ContentDeliveryManager` | SubscribedContent-310093Enabled | 0 | 1 |
| app-suggestions | same | SubscribedContent-338388Enabled | 0 | 1 |
| windows-tips | same | SubscribedContent-338389Enabled | 0 | 1 |
| settings-suggestions | same | SystemPaneSuggestionsEnabled | 0 | 1 |
| lock-screen-images | same | RotatingLockScreenEnabled | 0 | 1 |
| silent-app-installs | same | SilentInstalledAppsEnabled | 0 | 1 |
| dynamic-search-box | `...\CurrentVersion\SearchSettings` | IsDynamicSearchBoxEnabled | 0 | 1 |
| advertising-id | `...\CurrentVersion\AdvertisingInfo` | Enabled | 0 | 1 |
| tailored-experiences | `...\CurrentVersion\Privacy` | TailoredExperiencesWithDiagnosticDataEnabled | 0 | 1 |
| language-list-access | `HKCU\Control Panel\International\User Profile` | HttpAcceptLanguageOptOut | 1 | 0 |

Excluded on purpose for this batch: every Policies key (HKCU and HKLM), HKLM
values (edge-shortcuts, hags, consumer-features, edge-sidebar, Recall,
activity history, Copilot policy), string-typed accessibility Flags,
delete-to-restore values (feedback-frequency, activity-history), grouped
toggles (lock-screen-ads, preinstalled-apps, settings-suggested-content,
edge-debloat, bing-search), and anything with a restart requirement
(game-dvr, copilot-button, hags, sticky and filter keys), because a
background restore cannot restart Explorer, sign out, or reboot.

The first catalog also defers auto-game-mode and xbox-game-tips. They meet
these structural rules, but automatic gaming changes remain outside the
initial eleven-target batch. They are explicit exclusions in the parity test.

Parity: RestorationCatalogParityTests in Modules.Annoyances.Tests compares
all catalog entries with the real reader, card provider, change factory, and
baseline store. It checks both desired values, identity, enforcement, restart
requirements, and rejection of changed output without copying a value table.

## Shipped: shared reversible execution contract (2026-09-06)

Core only. No service worker, no live write, no consent, no App DI change.

- `IReversibleChangeExecutor` and `ReversibleChangeExecutor`
  (`src/ThisIsMyPC.Core/Changes`): one descriptor in the apply or revert
  direction. `Enforcement != null` goes through `IEnforcementExecutor`
  (`ExecuteAsync` or `RevertAsync`, the caller's delegate passed along);
  `null` calls the supplied module delegate directly. Nothing else is
  inferred: no module lookup, no registry writer. An enforced descriptor
  with no enforcement executor returns a failed result and never calls the
  delegate; `CanExecute` lets a batch refuse up front. Exceptions from the
  delegate or the enforcement executor propagate unchanged. The contract has
  no cancellation token on purpose: the pending pipeline has none, and a
  token here would reach only enforced changes, never a bare delegate. The
  enforcement executor is called without a token, exactly as before the
  extraction; a test pins that.
- `PendingChangesService` keeps its public API and constructor and routes
  every apply and rollback through the contract. Group order, mid-group
  rollback with Before/After-swapped descriptors, and queue bookkeeping did
  not change. `PendingChangesService.Create(IReversibleChangeExecutor)`
  builds a second queue on the same executor; restoration must use its own
  instance, never the app's shared queue. It is a static factory on purpose:
  MS DI resolves `IPendingChangesService` by constructor, and two
  satisfiable single-parameter constructors would be ambiguous. App DI stays
  `AddSingleton<IPendingChangesService, PendingChangesService>()`.
- `ChangeHistoryService` undo and redo route through the same contract. The
  only observable difference: an enforcement revert that fails without a
  message now reads "Enforcement revert failed" (the pending queue's wording)
  instead of "Enforcement execution failed".
- `RestorationBatchFactory` (`src/ThisIsMyPC.Core/Drift`):
  `Prepare(candidate, observedSnapshot)` builds the descriptor restoration
  runs. `RestorationCandidate` is a record, so a caller can rebuild it with
  `with` after validation; preparation therefore trusts nothing on it. The
  target must be `RestorationCatalog.Default`'s own instance at that key and
  value name (a structural copy, a custom catalog, or a `with` copy fails),
  the desired value must be canonical DWORD data in that target's allowed
  list, the SID must pass the account-SID shape test, and the resolved path
  must equal the path derived from the target and SID. Any miss is
  `UntrustedCandidate`, checked before the snapshot is looked at. The
  descriptor's `SystemLocation` is derived from the catalog target and the
  checked SID (`HKU\{sid}\...` plus value name), never copied from the
  candidate; that is the only key a SYSTEM process can address, and the
  HKCU form is the interactive app's view of the same key. The before value
  is the canonical observed data; `AfterValue` is the desired value;
  enforcement is null (validation already refused anything but reversion
  vectors); restart requirement is None. Other outcomes: `AlreadyMatches`
  (no write), `ObservedKindMismatch` (present but not DWORD data, or data
  that does not canonicalize), and `ObservedAbsent`.
  `RestorationPreparation` is a sealed class, not a record: private
  constructor, get-only properties, no public static members, creation only
  through internal methods the factory calls. Core has no
  `InternalsVisibleTo`, so the Core assembly is the trust boundary and no
  outside code can wrap an unchecked descriptor in a Ready preparation. A
  reflection test pins that surface. `CreateGroup(preparations)` accepts
  only ready preparations, throws on any other outcome or a null entry, and
  copies the descriptors into a read-only list. Ordinary app groups are
  still built by hand; this is the validated restoration boundary only.
- Tests: `ReversibleChangeExecutorTests`, `PendingChangesSharedExecutorTests`
  (two-queue isolation, shared executor, rollback order and swapped values,
  rollback failure, exception path, IsApplying), `RestorationBatchFactoryTests`
  (descriptor shape, canonicalization, no-op, absent and kind refusals;
  forged candidates: altered path, altered SID, non-account SIDs with a
  matching path, target rebuilt with another key, value name, identity, or
  wider allowed list, desired value of the wrong kind, non-canonical, or off
  the list, custom-catalog candidate, and a custom-catalog copy of a shipped
  target; group rules; stage and apply through an isolated queue; rollback
  writes the observed value back). Existing `PendingChangesServiceTests`,
  `PendingChangesEnforcementTests` and `ChangeHistoryEnforcementTests` pass
  unchanged.

What this step does not prove: the shared contract guarantees one routing
path, not one registry writer. The app writes through module delegates
(`AnnoyancesModule.ApplyChangeAsync` over the app's `IRegistryService`); the
service has no module and its delegate does not exist yet. Until the worker
step supplies that delegate and a test runs a prepared descriptor through
it, "same code as the app" means same routing, rollback, and before-state
contract only.

The existing history path is not ready for restoration descriptors as is.
`DriftBaselineStore.RecordApplied` keys entries by `SystemLocation` and
stamps the document with the one `_userSid` it was built with. A
restoration descriptor carries the `HKU\{sid}\...` location, so feeding it
through ordinary history recording would add a parallel HKU entry beside the
HKCU one, and `RestorationCatalog.Validate` rejects HKU locations, so the
next boot would skip it. Restoration outcomes therefore need their own
identity: the canonical HKCU location (module id, setting id, catalog key
path, value name) plus the target SID, kept as two fields in the journal
(step 3), the import (step 5), and any undo entry. Mapping back to the HKCU
form must happen before anything touches `RecordApplied` or the app's
history, and only for the profile the store's SID names; other profiles'
outcomes stay in the journal until the machine-scoped baseline exists.
None of that is wired; no service caller exists yet, so the work belongs to
history integration, not to this step.

### Exact dependencies left by this step

- **Absent before-state.** `ChangeDescriptor.BeforeValue` is a required
  string with no absent state. The Annoyances module treats an empty
  `AfterValue` as "delete the value" and the drift report shows `__absent__`;
  both are sentinels. `Prepare` therefore refuses an absent observation
  (`ObservedAbsent`) instead of writing "" or "__absent__" into a descriptor.
  Restoring a value Windows deleted needs an explicit absent representation
  on the descriptor (or a parallel typed field) that the module delegate,
  history, and the baseline all honor. That is its own step; until then a
  deleted catalog value is reported, not restored.
- **The delegate the service supplies.** `ThisIsMyPC.Service` references
  Core and Interop.Win32 only, so no module `ApplyChangeAsync` exists there.
  The service worker (step 9 below) must supply a delegate for the shared
  executor that writes exactly the descriptor's `SystemLocation`,
  `ValueType`, and `AfterValue` through `IRegistryService` under the resolved
  `HKU\{sid}` path, and nothing else. Writing that delegate is part of the
  worker step, gated by steps 2 through 8; it is not a second execution path
  because routing, rollback, and before-state stay in the shared contract.
- **Batch cancellation (existing flaw, separately scoped).**
  `IPendingChangesService.ApplyAllAsync` takes no `CancellationToken`, and an
  exception thrown by a delegate or the enforcement executor mid-batch is not
  a failed result: no rollback runs, no applied group is removed from the
  queue, and only `IsApplying` resets. `PendingChangesSharedExecutorTests`
  pins this so a change to it is deliberate. Restoration needs a
  cancellation-aware batch (disable waits for the running operation, step 2)
  and that means adding a token to the pipeline and defining whether a
  cancelled batch rolls the current group back. Do that as its own change
  with the app's Apply flow in view; do not change it silently.

## Remaining work, in dependency order

Each step depends on the ones above it. Step 1 shipped as described above;
nothing below is started.

1. **Shared reversible execution contract.** Done (2026-09-06, above).
2. **Thread-correct cross-process coordination.** A Windows mutex is
   thread-affine; do not hold one across async continuations. Use one
   dedicated owning thread, or a hardened lock with an explicit lifetime
   (a locked file under the hardened ProgramData folder is acceptable).
   Apply, undo, redo, baseline write, consent change, history import, and
   journal rotation all take the same lock. Disable waits for the running
   operation, then no new restoration write may start; consent is rechecked
   under the lock before every write.
3. **Durable intent and outcome journal.** Write intent (target, SID,
   observed snapshot, desired value, attempt id) before the write and the
   outcome after it, under the lock, fsync both. On start, an intent with no
   outcome is uncertain: read the value again; a matching value is observed
   recovery, not proof the service wrote it. An uncertain intent becomes a
   diagnostic conflict record that keeps the original intent for explicit
   recovery. It must never create an ordinary undo entry, because that
   would roll back an unrelated writer.
4. **Non-lossy retention.** Never rotate or truncate unimported intents or
   outcomes to meet a size limit. When retention cannot continue, stop new
   attempts and report storage pressure; do not drop records.
5. **Transactional history import.** The app imports service outcomes into
   change history in one transaction per journal segment, keyed by attempt
   id so a repeated import is a no-op. Import marks the segment consumed
   only after the history commit succeeds.
6. **Conservative management exclusion.** Management uncertainty applies to
   every catalog target, preferences included; a non-Policies path is not
   proof of an unmanaged value. Detect domain or MDM management and skip
   uncertain targets rather than fight them. This is a per-target decision
   recorded in the journal, not a global switch.
7. **Durable machine consent.** Consent is machine-scoped, stored under the
   hardened ProgramData folder, owner-checked the same way the baseline is.
   Baselines and history keep their target SIDs; unloaded profiles are
   skipped, and no hive is loaded for restoration in this batch.
8. **Limited retry.** Three failed or repeatedly reverted attempts per
   target in seven days stop further attempts for that target. Count
   failures the same way everywhere. Reset is explicit and lands in the UI
   step below.
9. **Service worker.** A hosted worker in `ThisIsMyPC.Service` that runs
   after the drift scan: load baseline (trust check as today), validate each
   entry through `RestorationCatalog.Default.Validate`, snapshot the value
   through `RegistryValueSnapshot.FromRead` against the `HKU\{sid}` path,
   skip matches, build descriptors with `RestorationBatchFactory.Prepare`,
   stage them as one group on the service's own `PendingChangesService`
   (`Create` over a `ReversibleChangeExecutor`), and apply with the
   registry-writing delegate described above under steps 2 through 8. Off
   unless consent says on. The IPC envelope gains new message types for consent
   state and restoration outcomes; existing types do not change.
10. **UI wiring.** Settings > Owner Mode gains the consent switch (off by
    default, machine-scoped wording), a per-target retry reset, and the
    conflict list from step 3. Card callouts describe restoration only when
    consent is on. Sight-harness screenshots before commit, as for every UI
    change.

Verify each catalog target and edition independently before any target
gains an enforcement claim. Factory metadata parity does not establish real
Windows enforcement support.

## Concrete blockers found in this batch

- `ThisIsMyPC.Core.Tests` cannot reference `ThisIsMyPC.Modules.Annoyances`
  without a layering change; the compile-checked parity test goes in the
  Annoyances test project instead.
- `EnforcementJson` is internal to Core, so tests build enforcement JSON by
  hand or round-trip it through `DriftBaselineStore`. Both are covered.
- The drift report's `__absent__` sentinel and the baseline's empty-string
  `ExpectedValue` still mean "no value" in the watchdog. Restoration refuses
  both as desired values, so a delete-to-restore target needs an explicit
  Absent desired state in a later catalog revision, not a sentinel string.
