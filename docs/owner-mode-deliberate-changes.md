# Deliberate changes and Owner Mode

Step 4 of the [single-user plan](planning/owner-mode-single-user-plan.md) connects deliberate app changes to the protected baseline.

## Transaction boundary

Apply, rollback, history persistence, and chosen-value persistence share one machine lease. Undo and redo use the same boundary. Delegates do not acquire another lease.

After acquisition, supported catalog targets are read again. A changed or unreadable before-value refuses the operation. This revalidation covers the eleven existing catalog targets, not every module setting.

Before the first system write, affected choices are removed durably from the strict baseline. Unaffected choices remain. Successful changes become protected choices only after history succeeds and the live typed values match. A crash, failed write, failed history insert, or failed baseline save cannot retain an old choice for an affected target.

## Pause

Pause acquires the same lease and saves explicit consent-off. It does not require journal recovery, so a corrupt journal cannot prevent opt-out. The lease is released before service shutdown. Failed consent persistence cannot report a successful pause.

## Production boundary

Automatic restoration remains unregistered. The temporary production app-only recovery path saves consent-off before deliberate work. It does not reconcile the restoration journal and must not authorize automatic restoration. Consent remains off after deliberate changes in this preactivation batch.

Step 5 must replace that app-only recovery boundary with trusted shared recovery before connecting restoration consent and status. Unsupported journal or management evidence must continue to block automatic writes.

## Verification

Fourteen coordinator tests cover stale staging, exact queue snapshots, persistence failures, rollback, service exclusion, and undo/redo. Five Pause tests cover acquisition ordering and failed persistence. Five command tests cover readable refusal, cancelled shutdown waits, and active-write completion. The Annoyances suite passes 153 tests, including absent-value undo across the catalog.

The full CI-safe suite, Release build, and fresh review pass. Main-window staging and dark/light history screenshots were inspected. Native shutdown, SCM interaction, and live host changes were not exercised in this batch.
