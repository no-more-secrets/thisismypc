# Deliberate changes and Owner Mode

The unelevated UI sends approved changes to the elevated Broker. Software integration is complete;
installed-service live evaluation remains untested.

## Transaction boundary

Each broker change uses the shared machine lease through recovery, before-value validation, mutation, and protected-choice persistence.
Undo, redo, and rollback commands use the same boundary. The UI keeps its ordinary history in LocalAppData;
service restoration history uses protected ProgramData storage and is displayed through read-only IPC.

The eleven catalog targets are read again after acquisition. A changed or unreadable before-value refuses the operation.
Affected protected choices are removed durably before mutation. Successful typed values become the new protected choice.
Unsupported or absent choices remain unprotected. Other catalog choices are preserved.

An ambiguous baseline save is disarmed before rollback. If the affected choices cannot be removed,
consent-off must be saved before rollback. Failed rollback or failed disarming reports the uncertainty explicitly.
A different administrator account cannot write the bound user's protected catalog settings.

## Pause and recovery

Pause saves explicit consent-off under the machine lease without requiring journal recovery.
The lease is released before service shutdown. Failed persistence cannot report a successful pause.
Broker control sessions do not initialize the module graph merely to pause.

Shared native recovery replaces the temporary consent-off-on-every-apply path.
Clean recovery preserves consent. Interrupted or uncertain restoration retains diagnostic evidence and blocks further writes.

## Historical Step 4 verification

Fourteen coordinator tests cover stale staging, exact queue snapshots, persistence failures, rollback, service exclusion, and undo/redo. Five Pause tests cover acquisition ordering and failed persistence. Five command tests cover readable refusal, cancelled shutdown waits, and active-write completion. The Annoyances suite passes 153 tests, including absent-value undo across the catalog.

The full CI-safe suite, Release build, and fresh review pass. Main-window staging and dark/light history screenshots were inspected. Native shutdown, SCM interaction, and live host changes were not exercised in this batch.
