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
- **Batch cancellation.** Shipped 2026-09-06 as the next section. The app's
  Apply flow does not pass a token yet; the wiring it needs is listed there.

## Shipped: exception-safe and cancellable batches (2026-09-06)

Core only: `PendingChangesService`, `IPendingChangesService`,
`ChangeHistoryService.RecordChangesAsync`, `MutationResult`, and three new
types in `src/ThisIsMyPC.Core/Changes` (`MutationFailureKind`,
`RollbackFailure`, `GroupReconciliation`). No App, service, IPC, or mutex
work; no live write.

The bug this fixes: a delegate or enforcement executor that threw mid-batch
propagated out of `ApplyAllAsync`. No rollback ran, groups that had already
finished stayed in the queue (a retry replayed them), and only `IsApplying`
reset. The test that pinned that behavior is replaced by the contract below.

- **A thrown apply is a reported failure, not an exception.** The result has
  `FailureKind = ChangeThrew`, `Failed` set to the change, `Exception` set,
  `ErrorCategory = ServiceUnavailable`, and the change in `Uncertain`. It
  may or may not have written, so it is never reverted and never appears in
  `RolledBack`; the group's earlier successes are rolled back as for a
  returned failure. A thrown `OperationCanceledException` is treated the
  same way, even when it matches the batch's own token: the batch cannot
  know whether the delegate checked the token before or after writing.
- **A failed result is a report, not evidence.** Modules catch their own
  exceptions and return failure, and `EnforcementExecutor` can write the
  primary value and then fail on cache or companion restoration. So the
  change that returned failure is uncertain too (`FailureKind =
  ChangeFailed`, listed in `Uncertain`), and a rollback failure is
  uncertain whether the revert returned failure or threw. The only positive
  evidence the interface supplies is a success result, which is what
  `Applied` and `RolledBack` are built from. `MutationResult.Uncertain`
  lists, in group order, the failed or thrown change plus every rollback
  failure's change; `HasUncertainState` is true when it is non-empty.
- **An uncertain group needs reconciliation before anything applies.** The
  queue keeps such a group staged and visible, keeps a
  `GroupReconciliation` record beside it (kind, failed change, uncertain
  list, rolled back, rollback failures, message, exception, time), and
  exposes those records as `IPendingChangesService.ReconciliationRequired`
  (a default interface member returning empty, so fakes compile). While any
  is staged, `ApplyAllAsync` returns `FailureKind = ReconciliationRequired`
  with that record's diagnostics copied in and invokes no writer, not even
  for clean groups behind it. Only `DiscardAll` clears reconciliation records.
  `Unstage` retains marked groups without throwing or raising notifications.
  A delayed page callback cannot clear the block by replacing its group.
  After discard, the caller reads live values before staging a fresh group.
  Records use group instance identity; a fresh group inherits no old record.
  A clean group unstaged
  during its own failing apply is not marked (its diagnostics are still on
  the result). A cancelled batch whose reverts all returned success leaves
  a clean, retryable group; a cancelled batch with a rollback failure marks
  it.
- **One id, one staged group.** `Stage(ChangeGroup)` throws
  `InvalidOperationException` under the queue lock when a group with that
  id is already staged, whether it is the same instance or another one. A
  duplicate would make removal ambiguous and show one review-panel row for two
  batches of writes. App groups use fresh GUIDs. Card view models call
  `Unstage` before `Stage`; marked groups remain blocked until `DiscardAll`.
  `Stage(ChangeDescriptor)` mints its own id and cannot collide. Committed
  groups are removed from the queue by exact instance, not id: a person
  who removes a group and sets it again while the batch is applying gets
  the replacement kept in the queue after the original completes, and a
  replacement staged under an uncertain group's id is neither removed nor
  marked.
  Consequence for the app's Apply: after a returned failure the person can
  no longer click Apply again to retry the same staged group. They discard
  all from the review panel, reload, and stage again; wiring item 3 covers
  the message.
- **Cancellation is cooperative, between changes.** The new overload
  `ApplyAllAsync(applyFunc, revertFunc, CancellationToken)` checks the token
  before each change starts and nowhere else. The token is never passed to
  the executor or a delegate, so a change in flight always runs to completion
  and its result is honored. On cancellation the current group's applied
  changes are reverted (rollback runs with no token at all), the group stays
  staged, and the result is `FailureKind = Cancelled`, `WasCancelled = true`,
  `Failed = null`. A group whose last change completed before the check is
  committed, not rolled back. A token that is already cancelled stops the
  batch before the first write and leaves the queue untouched.
- **Finished groups are committed on every exit.** Success, returned failure,
  thrown apply, and cancellation all remove the completed groups from the
  queue by id and return their changes in `Applied` with their restart
  requirements. A retry applies only what is still staged. A second
  `ApplyAllAsync` while one is running throws `InvalidOperationException`
  instead of snapshotting the same groups twice (the actions queue already
  did this).
- **Rollback failures are captured one by one.** `RollbackFailures` lists
  each applied change that was not put back as a `RollbackFailure(Change,
  ErrorMessage, Exception)`. The exception, when the revert threw, is kept
  for diagnostics only; a null exception is not evidence of state. One
  revert throwing does not stop the remaining reverts.
- **History records committed groups on every exit.**
  `ChangeHistoryService.RecordChangesAsync` now records `Applied` whenever
  it is non-empty, whatever `IsSuccess` says. `Applied` only ever holds
  changes from groups that completed and left the queue, so this is the
  verified set; the failed change, rollback failures, and `Uncertain` are
  never written to history or to the drift baseline. Until the App calls it
  on failed results (wiring item 2), the change has no effect on the app.
- **Compatibility.** The two-argument `ApplyAllAsync` is unchanged for
  callers and forwards to the token overload with `CancellationToken.None`.
  The interface carries the token overload as a default member: it forwards
  an uncancellable token to the two-argument method and throws
  `NotSupportedException` for a cancellable one, so an implementation that
  ignores the token (the integration `FakePendingChangesService`) still
  compiles and can never be mistaken for a cancellable queue. `MutationResult`
  gains only defaulted `init` properties (`FailureKind`, `Exception`,
  `RollbackFailures`, `Uncertain`) and two derived flags; every existing
  construction site compiles unchanged. Successful batches produce exactly
  the result they did before, and a returned failure still leaves its group
  in `PendingGroups` as before; the one behavioral change for existing
  callers is that a second Apply over that group is refused until it is
  discarded.
- **Tests.** `PendingChangesBatchSafetyTests` (`tests/ThisIsMyPC.Core.Tests/
  Services`): apply throw mid-group, on the first change, and restart
  aggregation from committed groups only; rollback throw with the remaining
  rollbacks still running; rollback returned failure as uncertain; a
  returned failure marking the group even with clean rollbacks;
  cancellation before the batch, during the first change (clean, not
  marked), between changes with the in-flight call awaited and the revert
  seeing a cancelled token, after a group completes, and with a failing
  rollback (marked); a delegate that throws for the batch token; the
  two-argument overload; retry after a clean cancellation applying only
  what is staged; refusal after a thrown apply and after a failed rollback
  with no writer invoked and the record kept; `DiscardAll` clearing marks
  and a fresh group under the same id applying; a delayed replacement callback
  unable to remove a marked group or invoke a writer; staging under a marked
  id refused with the mark intact; the same
  instance and a different instance refused under a staged id; single
  changes never colliding and an unstaged id free again; a replacement
  staged mid-batch under a completed group's id staying queued; a
  replacement under an uncertain group's id neither removed nor marked; a
  group unstaged during its own failing apply not marked; marks on one
  queue not blocking another; a group staged mid-batch surviving; two
  queues on one executor with one cancelled; a concurrent second batch
  rejected; `IsApplying` reset on every path; the default interface members.
  `ChangeHistoryServiceTests` adds: a failed batch's `Applied` is recorded
  and nothing else is; failed, cancelled, and refused results with nothing
  applied record nothing. Full CI-safe suite and Release build pass.

### Caller wiring before cancellation reaches the UI

Nothing in App passes a token or reads the new result fields today, so this
section is what the App step must do. The Core result shape is stable for
it. It is listed here so it is not done by accident.

1. `MainWindowViewModel.ApplyAllAsync` (App) calls the two-argument overload.
   To make the person's Apply cancellable it needs a `CancellationTokenSource`
   per batch, a Cancel button bound to it while `IsApplying` is true, and the
   three-argument call. Cancel must not be offered while rollback runs; the
   service exposes no separate "rolling back" state, so the view model would
   have to treat a cancelled token plus `IsApplying == true` as "finishing".
2. The same method calls `RecordChangesAsync` only when `result.IsSuccess`.
   Since a failed, thrown, or cancelled batch still commits its finished
   groups (they left the queue and are in `result.Applied`), the App must
   call `RecordChangesAsync(result)` on every exit, not only on success, or
   those changes are applied with no undo entry. This gap predates this
   step (a returned failure after a finished group already lost its
   history); it is now visible because `Applied` is populated on more paths.
   Core's guard is already gone: the method records `Applied` and ignores
   the rest, so the App change is moving the call out of the `IsSuccess`
   branch. The drift baseline follows the same call.
3. `FormatApplyError` (App) reads `Failed`, `ErrorMessage`, and
   `ErrorCategory`. It should branch on `FailureKind`: `Cancelled` is not an
   error (status "Apply cancelled; N changes put back"); `ChangeFailed` and
   `ChangeThrew` must name the change and say its value is unknown;
   `ReconciliationRequired` must direct the person to Discard All in the review
   panel, reload, and set the change again; any non-empty `Uncertain`
   list must be shown by display name, because the group stays staged and
   the person will see it again in the review panel with no other
   explanation. The review panel should read
   `IPendingChangesService.ReconciliationRequired` and mark those groups.
4. Card view models may call `Unstage` before `Stage` for clean groups.
   `Unstage` retains marked groups, and `Stage` under a staged id throws.
   The page stays disabled while reconciliation is required. Delayed callbacks
   cannot clear the gate through `Unstage`. Discard All is the recovery path:
   clear the queue, reload the affected page, and stage from fresh live values.
   Reusing values scanned before failure would produce a stale `BeforeValue`.
5. Module delegates (`ApplyChangeAsync` in each module) catch their own
   exceptions and return failed results, so `ChangeThrew` is reached today
   only through the enforcement executor or a delegate bug. Keep it that
   way: a delegate should not observe the batch token, because a thrown
   `OperationCanceledException` is reported as uncertain, not as a clean
   stop.
6. Restoration (step 9) uses the concrete `PendingChangesService.Create`
   queue and passes the disable token from step 2. Disable therefore waits
   for the in-flight write and its rollback, exactly as the contract says.
   The worker must journal every result with `HasUncertainState` as a
   conflict (step 3), never as an ordinary outcome. After journaling, use
   `DiscardAll` on the isolated restoration queue to clear marked groups.
   Read live values before building any later restoration batch.

## Remaining work, in dependency order

Each step depends on the ones above it. Step 1 shipped as described above,
and the batch contract for step 2's disable path shipped with it; the
cross-process lock itself is not started.

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
