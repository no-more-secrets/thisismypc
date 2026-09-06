# Owner Mode history import

This Core-only batch imports terminal journal evidence. It does not start a worker or write registry values.

## Transaction and retry contract

`RestorationJournalImporter.ImportAsync` requires a held, recovered mutation lease throughout import and acknowledgement.
It reads the trusted journal directly. A faulty snapshot blocks all import; unmatched intents stay pending.
Recovery must first record diagnostic evidence and finish lease recovery.

Schema v3 adds attempt ID, profile SID, outcome, and detail to history rows.
A separate `owner_journal_imports` table retains the complete typed intent/outcome payload, transaction ID, history ID, and import timestamp.
Each attempt gets one immediate SQLite transaction containing both history and receipt inserts, with synchronous FULL enabled.
Only after commit does the importer append the journal acknowledgement.

Equal retries return the original transaction ID. Conflicting payloads or acknowledgement IDs fail closed.
A committed import with a failed acknowledgement can retry without duplicating history.
An acknowledged attempt without its database receipt fails closed, including after database loss.
Clearing visible history retains receipts and cannot recreate cleared rows from retained journal files.
No receipt reclamation is implemented.

## Identity and execution

History stores canonical HKCU location and original SID separately. The receipt preserves typed desired and observed values, including absent observations.
Display names come from the restoration catalog. The history timestamp is intent creation time, not a claimed completion timestamp.
Outcome and detail identify Applied, Failed, Uncertain, and RecoveryObservation evidence.
All imported rows currently use the diagnostic SystemReversion category and reject generic history execution, including Applied outcomes.
`CanExecuteHistoryAction` and `CanCreateCustomSet` expose the same conservative boundary to future App integration.
The Core history service checks that boundary before executing undo or redo delegates.

Import does not update the baseline. Restoring an existing expectation must not replace profile identity with the importing account.
Later ordinary undo needs SID-aware routing and canonical baseline persistence before these rows become executable.

## Integration limits

The caller supplies trusted journal storage and a trusted history database. SQLite transactions do not authenticate a replaced database.
The caller must prevent lease disposal during asynchronous work. Import never acquires or recovers the machine lease automatically.
App display, selection, and startup integration remain separate. Production Service wiring is absent.
No tests simulate actual power failure or enforce Windows filesystem ownership.
Tests use temporary journal files and SQLite databases, covering migration, transaction rollback, acknowledgement failure, duplicate import, conflict, clear, diagnostics, and profile identity.
