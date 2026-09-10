# Owner Mode: single-user completion checkpoints

Approved scope direction: Sam Boland, 2026-09-06. This plan supersedes multi-profile assumptions in earlier Owner Mode plans.

## Product contract

ThisIsMyPC targets one person managing their own PC. Owner Mode keeps explicitly selected supported settings at their chosen values.
Bind restoration to one primary Windows account. Keep its SID internally because the service runs as SYSTEM. Do not enumerate other accounts for restoration, merge their preferences, load their hives, or offer per-account controls.
Machine-wide policies retain their existing Windows scope. Do not copy arbitrary administrator preferences into other accounts. The current restoration catalog contains eleven HKCU DWORD targets, not machine-wide policy targets. This batch adds no targets.

Explicit consent defaults off. Pause prevents new writes and waits for an active attempt. Unknown or unsupported state produces a readable refusal.
Changes made through ThisIsMyPC update the chosen value. Changes outside the app cannot reliably identify the writer: explain that protected values will be restored, and provide pause before external edits. Do not promise to recognize Windows versus a person.

## Current status (2026-09-09)

Steps 1 through 6 have complete software integration. The UI is unelevated; the Broker and SYSTEM service
share native recovery and the mutation lease. Trusted storage, bound-owner loading, profile and management evidence,
explicit consent, status, and restoration history are connected. Only eligible saved catalog choices can restore.
Installed-service live evaluation remains untested. No live preference change is claimed by this completion record.

Verification: Release build and all 2,439 CI-safe tests passed. Sixteen elevated native tests passed,
including interrupted recovery. Fresh review found no blockers. Dark and light history screenshots were inspected.
Guarded App, Broker, and Service NativeAOT publishes passed. The App retains one existing Avalonia reflection-binding trim warning.

The earlier per-step authorization rule was superseded by Sam's instruction to complete Owner Mode autonomously.
The checkpoints below preserve the implementation sequence and its test requirements.

## Historical checkpoint assignments (2026-09-06)

Each completed batch used an implementation worker and independent review, with coordinator integration and verification.
No unsupported cost or time estimate forms part of the completion evidence.

## 1. Scope audit and plan

Assignment: one read-only agent; coordinator writes this plan.

Confirmed seams: `Service/Program.cs`, `Service/DriftWatchdog.cs`, `Core/Drift/DriftBaselineStore.cs`, `Core/Services/ChangeHistoryService.cs`, `App/Services/OwnerModeService.cs`.

Exit: remaining steps name implementation seams and tests. No product code changes.

## 2. Finish single-owner settings storage

Storage implementation: [single-owner baseline](owner-mode-single-owner-baseline.md). The new strict store and native adapter replace the experimental multi-profile design. Legacy observational storage is unchanged and must not authorize automatic restoration. Production caller routing remains part of Steps 3-4.

Assignment: one Core/native implementation agent; separate reviewer afterward.

Work:
- Bind the existing consent/baseline contract to one primary SID. Missing or mismatched identity must not authorize another account.
- Replace swallowed baseline persistence failures with explicit errors. Use the existing typed catalog and consent storage protections.
- Keep durable chosen values separate from observations. Refuse corrupt or untrusted storage; preserve evidence.
- Define the trusted journal/database access needed for the next step. Add only the storage adapter needed by this path.

Files: `Core/Drift/DriftBaseline.cs`, `DriftBaselineStore.cs`, `Core/Drift/Consent`, `Interop.Win32/Drift/Consent`, `Core/Services/ChangeHistoryService.cs`. Journal/database trust adapters must have a concrete caller, not a generic framework.

Tests: `Core.Tests/Drift/DriftBaselineStoreTests.cs`, consent tests, history service tests, and isolated native storage tests. Cover restart, primary-SID mismatch, missing/corrupt state, failure propagation, and no silent baseline overwrite.

Exit: one owner's chosen values survive restart, and failed persistence blocks restoration. No automatic host writes yet.

## 3. Prove one complete restore loop

Assignment: one implementation agent; coordinator prepares fake-registry integration coverage alongside it; independent review afterward.

Work: use one existing catalog target. Add a bounded periodic scan with a proposed 30-second default. Reread consent and live state after acquiring the lock. Run eligibility, durable intent, pending-change apply, verification, outcome, and history import. Unmatched intents remain diagnostic. Retain existing retry limits.

Files: `Service/DriftWatchdog.cs` or one focused restoration worker, `Service/Program.cs`, `Core/Drift/Journal`, `Core/Drift/Eligibility`. Profile support checks concern only the bound account. Unknown management evidence blocks automatic writes; preference paths alone do not prove unmanaged state.

Tests: extend `Ipc.Tests/DriftWatchdogTests.cs` and add fake-registry worker tests. No delay-based tests: inject time/scan trigger. Verify one restoration, no repeat write after a match, consent-off, missing profile, management uncertainty, write failure, and restart recovery.

Exit: complete fake-registry loop works. Keep production automatic writes disabled until Step 4 passes.

## 4. Coordinate deliberate app changes and pause

Assignment: one integration agent, then separate reviewer. This is the main remaining uncertainty.

Work: acquire once around App apply/rollback/history/baseline persistence; do the same for undo/redo. Never reacquire inside delegates. Revalidate staged before-values after waiting. A crash between a user write and baseline persistence must inhibit stale restoration until reconciled. Pause writes durable consent-off before service shutdown, without holding the lock during SCM shutdown.

Files: `App/ViewModels/MainWindowViewModel.cs`, `Core/Services/ChangeHistoryService.cs`, `App/Services/OwnerModeService.cs`, `Core/Coordination/MutationCoordinator.cs`, baseline/journal integration from Steps 2-3.

Tests: integration tests for apply/history/OwnerModeService plus worker tests. Cover App/service exclusion, stale staging, pause during an attempt, corrupt-journal opt-out, failed baseline persistence, and shutdown while waiting.

Exit: deliberate app changes become the protected choice. Pause confirms durable off. Crash tests never restore stale intent automatically.

## 5. Connect existing controls and status

Assignment: one UI/IPC agent; coordinator runs sight tests; separate reviewer afterward.

Work: use the existing Owner Mode section. Show enabled, paused, unavailable, and conflict states. Explain protected external edits. Distinguish running service from consent to restore. Use existing history presentation; do not build a new dashboard.

Files: `App/ViewModels/OwnerModeSectionViewModel.cs`, existing Settings view, `Ipc.Contracts` message types, service pipe dispatch, `App/Services/OwnerModeService.cs`.

Tests: `App.UiTests/ToastAndOwnerModeShotTests.cs`, IPC tests, lifecycle tests. Inspect dark/light screenshots and applicable edge geometry. Verify failed enable/disable never shows success.

Exit: enable/pause and status work through the complete fake-backed service path.

## 6. Connect native trust and validate live behavior

Assignment: one test/review agent; coordinator fixes integration findings and prepares the owner's click-through check.

Supply trusted journal/database access, bound-account loading from the trusted baseline, and native loaded-profile/management evidence. Replace the temporary app-only consent-off recovery with shared recovery before registering automatic restoration. Step 5 deliberately leaves this native graph unavailable.

Run the existing eleven-target parity suite, full CI-safe suite, Release build, and isolated elevated coordination/storage tests. Expand the worker's supported subset only as target tests pass.

Prepare one controlled live target with captured before-state and an explicit rollback. Ask for the owner's live evaluation only after the implementation is concrete. Do not change live preferences merely to test them without that evaluation step.

Exit: verified subset restores correctly, pause works, history is truthful, and failures remain visible. Unsupported cases stay disabled.

## Historical Step 5 checkpoint

Steps 1 and 2 are complete. Step 3 implements one fake-backed catalog target through the coordinator, strict baseline, eligibility, durable intent, pending changes, verification, and history import. Production registration stays disabled.

The initial loop also caps total attempts at three per seven days. This conservative ceiling includes successful attempts; it does not classify an external edit as a Windows reversion. Uncertain outcomes and unmatched intents block further writes. Step 4 is complete: [deliberate changes and Pause](../owner-mode-deliberate-changes.md). Production app-only recovery saves consent-off and does not reconcile the restoration journal. Step 5 is complete for the fake-backed service path: [controls and status](../owner-mode-service-controls.md). Native journal/database/profile/management adapters remain absent. Step 6 must supply them and shared recovery before production restoration can run.

## Historical Step 6 pause (2026-09-06)

Added strict saved-owner resolution and shared interruption recovery. Clean recovery preserves consent.
Interrupted attempts disable consent and become diagnostic observations, never inferred successful restores.

Added a native storage scope for the journal and history database. Initial elevated tests exposed metadata-only
handles that did not prevent replacement. That checkpoint tried read access for file and directory pins;
its elevated rerun was canceled. No live preferences changed.

The later correction in `c82fed1` keeps directory handles metadata-only so child files can be replaced atomically.
Only file pins request GenericRead. The current sixteen elevated native tests passed with that correction.

The CI-safe suite and Release build pass in `artifacts/diagnostics/owner-step6-isolated`, based on a433cf2
plus the scoped changes. Concurrent hardening dependency edits still prevent root restoration.
At that checkpoint, the changes remained uncommitted pending native verification.

Remaining at that checkpoint: validate corrected native storage, connect every history writer with pooling disabled and PERSIST
journal mode, provide native profile/management evidence, register the shared service graph, then prepare
the controlled live evaluation. Production restoration remains unavailable.

## Deferred scope

Multi-user restoration, per-profile preferences, hive loading, machine-policy catalog expansion, journal compaction, real-time registry subscriptions, new hardware targets, generic storage frameworks, and automatic history undo for imported SID-bound records.

Shared locking, durable consent, failed-write handling, and crash inhibition remain necessary even for one user: the app and service are still separate processes.
