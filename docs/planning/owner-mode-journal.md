# Owner Mode journal foundation

Historical foundation record. Statements about unwired production code describe the original checkpoint.
Current software integration and remaining live evaluation are recorded in the
[single-user plan](owner-mode-single-user-plan.md).

This batch adds Core persistence contracts only. No App, service, IPC, registry writer, consent, or history importer calls them.

## Write protocol

1. Acquire the shared machine mutation lease.
2. Read the journal before marking the lease recovered, even after a clean acquisition.
3. Stop if the snapshot has faults. Its retained prefix is diagnostic only.
4. Investigate unmatched intents and flush recovery observations. A matching value does not identify its writer.
5. Mark the lease recovered after the recovery policy completes.
6. Recheck consent, management exclusions, target identity, and current state under the lease.
7. Call `Begin` with a stable attempt ID, validated candidate, and typed observed snapshot.
8. Write only when `Begin` returns a new `DurableIntent` permit.
9. Keep the lease through the system write and any rollback.
10. Call `Complete` after the write with its observed result.

`Begin` revalidates candidates through `RestorationBatchFactory`. It records canonical HKCU identity and target SID separately.
The observation is typed data. Absent recovery observations remain null typed values, never string sentinels.
This batch refuses absent before-values because the existing restoration factory cannot execute them reversibly.

A permit belongs to its issuing journal instance and lease acquisition. Duplicate attempts return no permit, including after restart.
The same ID with different intent data fails. Identical completion and acknowledgement retries do not append duplicate records.
An Applied outcome requires a post-write observation equal to the intended value. Failure and uncertain outcomes remain diagnostic.
These contracts validate reports; they cannot prove a caller actually performed or observed a system write.

## Persistence and recovery

Each attempt owns one GUID-named `.tipj` file. Files append intent, outcome, then acknowledgement, in that order.
Each frame contains magic, bounded payload length, source-generated JSON, and a SHA-256 checksum.
The file uses `WriteThrough`, followed by `Flush(flushToDisk: true)` before a successful API return.
No new dependency or reflection serializer is used.

An incomplete final frame leaves complete prefix records readable as diagnostics and blocks further writes and import.
Invalid checksums, versions, identities, ordering, catalog data, or unexpected directory entries also block progress.
No automatic truncation or repair runs. A separate recovery procedure must preserve evidence before repairing damaged storage.
Checksum coverage detects damaged payloads; it is not authentication against an attacker who can rewrite the file.

An unmatched intent is uncertain even if its target now matches the desired value.
`RecordRecoveryObservation` records that observation as a diagnostic conflict. It cannot produce an Applied outcome or ordinary undo eligibility.
Recovery observations append under a held, unrecovered lease, without repairing directory DACLs.
This narrow exception permits diagnostic evidence only. New intents, ordinary outcomes, and import acknowledgements still require `CanWrite`.
Mark the lease recovered only after the recovery policy and its durable evidence are complete.

An I/O exception propagates. The caller stops the operation and reopens the journal before retrying anything.
An exception after a flush can leave a complete record despite the caller receiving no success response.
Attempt IDs and identical retry handling make that ambiguous return safe without repeating a system write.

## Trust boundary

Construction requires `IDataDirectoryGuard` and an explicit `Func<string, bool>` trust predicate. Neither has a permissive production default here.
The existing directory must pass trust checking before reads or hardening. Each existing or newly created segment also passes trust checking.
Recovery reads never invoke a potentially repairing directory guard while the lease remains unrecovered.
The predicate must verify existing directory protection and ownership, file ownership, and reparse-safe ancestry.
An owner-only baseline predicate is insufficient for the directory requirement.
Production integration must provide that implementation and prevent path replacement between checks and file opens.
A path under ProgramData, a checksum, or an admin-owned filename alone proves none of those properties.

The lease interface remains a trusted caller contract. Callers must not dispose it concurrently with journal operations.
Journal methods serialize calls within one instance. The shared machine lease coordinates other instances and processes.

## Capacity and import

The journal reserves three maximum-size frames before accepting each intent, including space for its outcome and acknowledgement.
The default storage budget is 16 MiB. Each frame payload is limited to 16 KiB.
New attempts stop when reservation capacity runs out. Existing records are never removed to admit another attempt.
At capacity, an accepted attempt can still complete and receive its import acknowledgement.

The history importer must transactionally deduplicate by attempt ID before calling `AcknowledgeImported`.
Only a committed import, including a diagnostic import, authorizes acknowledgement. An unmatched intent cannot be acknowledged directly.
Acknowledgement persists the import transaction ID but does not delete the file.
Acknowledged segments remain durable idempotency receipts in this batch and still count toward the storage budget.

## Remaining integration

- Implement hardened production trust checks and validate actual filesystem durability assumptions on supported Windows storage.
- Define recovery decisions and evidence-preserving repair for incomplete or corrupt tails.
- Add transactional history import with canonical target identity and profile SID mapping.
- Add explicit acknowledged-only reclamation that retains durable attempt-ID tombstones or an equivalent importer ledger.
- Wire consent, management checks, retries, and the isolated restoration worker only after their contracts are complete.

Tests use private temporary directories and fake trust and lease providers. They do not exercise Windows ACL enforcement or actual power loss.
