# Owner Mode cross-process coordination

Historical foundation record. Statements about unwired production code describe the original checkpoint.
Current software integration and remaining live evaluation are recorded in the
[single-user plan](owner-mode-single-user-plan.md).

Step 2 of `owner-mode-restoration.md`: one machine-wide mutation lease that
the interactive app and the Session 0 service take around every write that
restoration and the app's own pipeline share. This document records what
shipped (2026-09-06), why it is shaped this way, and the contract the later
steps (journal, consent, service worker, App wiring) must honor. Nothing is
wired: no App or Service code takes the lease yet, and no production lock
object is created by any test.

## Shipped

Core (`src/ThisIsMyPC.Core/Coordination`, pure, no Win32):

- `IMutationLeaseProvider.AcquireAsync(maxWait, token)`: awaitable, bounded
  (finite, non-negative wait; zero probes), cancellable. Every outcome is a
  returned `MutationLeaseResult`; only argument errors throw.
- `MutationLeaseResult`: sealed, private constructor. `Held(lease)` reads the
  outcome from the lease itself (`Acquired` or `AcquiredAbandoned`), so the
  result and the lease cannot disagree; `TimedOut`, `Cancelled`, `Refused`,
  `Faulted` carry no lease. `IsAcquired` is "lease non-null"; `CanWrite`
  forwards to the lease.
- `IMutationLease`: `LeaseId` (journal it with each intent), `Name`,
  `OwnerLabel` ("app" or "service"), `AcquiredAt`, `IsHeld`, `WasAbandoned`
  (diagnostic), `RequiresRecovery`, `CanWrite`, `MarkRecovered()`.
  `IDisposable` and `IAsyncDisposable`; disposing releases.
- **Every lease starts unable to write.** `RequiresRecovery` is true and
  `CanWrite` false on every acquisition, clean or abandoned. Only
  `MarkRecovered()` flips them, and only for that acquisition. The review
  that fixed this found that the kernel's abandoned flag cannot gate
  recovery: a holder that releases without recovering consumes the flag, so
  the next holder would see a clean acquisition; and a sole holder that
  crashes closes the last handle, which destroys the object, so the next
  acquisition creates a fresh mutex that looks clean. `WasAbandoned` is kept
  as a diagnostic for the log and the journal, nothing more.
- `MutationLeaseBase`: the one held-state machine both the Windows lease and
  test fakes derive from. Held from construction; the first `Dispose` or
  `DisposeAsync` runs the release exactly once (concurrent disposers included)
  whether or not the lease was cleared; `MarkRecovered` throws when the lease
  is not held or was already cleared; after disposal `CanWrite` is false.
- `MutationLeaseNames`: `Production` is
  `Global\ThisIsMyPC.OwnerMode.MutationLease` (global namespace so Session 0
  and the user session contend for one object). `ForTest()` mints
  `Local\ThisIsMyPC.Test.MutationLease.{guid}`. `IsValid` accepts only
  `Global\` or `Local\` plus one printable-ASCII segment of at most 200 chars.
  No test may reference `Production`.

Windows (`src/ThisIsMyPC.Interop.Win32/Coordination`):

- `NamedMutexMutationLeaseProvider(name, ownerLabel)`: the lease over a named
  kernel mutex. `LibraryImport` only, `DefaultDllImportSearchPaths(System32)`
  on every call, no new package, NativeAOT-safe (no `System.Threading.Mutex`,
  no `MutexAcl`, no reflection).
- `MutexSecurityVerifier`: reads the handle's owner and DACL with
  `GetSecurityInfo` and refuses anything but owner SYSTEM or Administrators,
  a non-null DACL of exactly two access-allowed entries, one for SYSTEM and
  one for Administrators, each `MUTEX_ALL_ACCESS` (or unmapped `GENERIC_ALL`).
  Any deny entry, extra trustee, weaker mask, or other owner is a refusal.
- `NativeMutationLease`: `CreateMutexExW`, `ReleaseMutex`, `CreateEventW`,
  `SetEvent`, `WaitForMultipleObjects`, `CloseHandle`,
  `ConvertStringSecurityDescriptorToSecurityDescriptorW`, `GetSecurityInfo`.
  ACE walking reuses `Security.NativeSecurity`.

### Why a dedicated owner thread

A Windows mutex is owned by the thread that satisfied the wait, and only that
thread may call `ReleaseMutex`. An `async` caller resumes on whatever thread
the scheduler picks, so a lease released on an async continuation would fail
with `ERROR_NOT_OWNER` (or, worse, release from the wrong thread by luck in
tests and fail in production). The provider therefore starts one dedicated
thread per acquisition (256 KB stack, background, named
"ThisIsMyPC mutation lease owner"). That thread builds the security
descriptor, creates or opens the object, verifies it, waits on
`[mutex, cancelEvent]` with the bounded timeout, and on success completes the
caller's task and parks on a release gate. `Dispose` or `DisposeAsync` sets
the gate from any thread and waits (10 s bound, logged on overrun) for the
owner thread to call `ReleaseMutex`, close the handles, and exit. The
cancellation token's callback only sets the unmanaged event, guarded by a
lock so it can never touch a closed handle. Task completion sources run their
continuations asynchronously so the caller's code never runs on the owner
thread.

A mutex was chosen over a locked file under the hardened ProgramData folder
because the kernel object needs no file to exist or be trusted before the
first write, its DACL is verified through the handle in one call, and
`WAIT_ABANDONED` gives a free diagnostic when a holder dies while another
handle keeps the object alive. That diagnostic is not the recovery signal;
the journal (step 3) is, for every acquisition.

### Trust rule, exact behavior

- The object is always created with `D:P(A;;GA;;;SY)(A;;GA;;;BA)`. There is
  no second descriptor and no retry with a wider one.
- Whether the create call made the object or opened an existing one (the
  `ERROR_ALREADY_EXISTS` case is the normal second-process path), the
  security is read back through the handle and verified. Only a verified
  object is waited on.
- `ERROR_ACCESS_DENIED` on open (a pre-existing object whose DACL excludes
  us, or a non-elevated process) and `ERROR_INVALID_HANDLE` (the name is held
  by another kernel object type) are `Refused`. `ERROR_PRIVILEGE_NOT_HELD`
  (the `Global\` namespace needs `SeCreateGlobalPrivilege`) and any other
  create or wait failure are `Faulted`.
- The verifier reads the owner before the DACL. An object created by a
  non-elevated process is owned by that user's SID and is refused on that
  ground; an owner can always rewrite its own DACL, so the owner check is
  not redundant with the ACE check.
- Cancellation never yields a lease. `WaitForMultipleObjects` returns the
  lowest signalled index, so a free mutex would beat an already-set cancel
  event; the owner thread therefore checks the token right before the wait
  and again after the wait returns. A mutex granted in the same instant the
  token was cancelled is released on the owner thread and the result is
  `Cancelled`. If that grant carried the abandoned flag, the flag is consumed
  (logged); recovery is mandatory for the next holder anyway.

## Integration contract for later steps

Everything below is required of the code that takes the lease. None of it
exists yet.

**What the lease spans.** One lease acquisition covers the whole of one of
these operations, start to finish, in both processes:

| Operation | Holder | Span |
| --- | --- | --- |
| Consent read before write, consent change | app, service | read consent, decide, write consent |
| Baseline write (`DriftBaselineStore`) | app | build document, write, verify |
| Journal intent and outcome (step 3) | service | write intent, fsync, write value, write outcome, fsync |
| Apply (`ApplyAllAsync`), undo, redo | app | from before the first write to after history is recorded |
| Restoration batch (step 9) | service | consent recheck, snapshot, prepare, stage, apply, journal |
| History import (step 5) | app | read segment, commit history, mark consumed |
| Journal rotation and retention (step 4) | service | whole rotation |

Consent is rechecked under the lease before every write. Disable takes the
lease, so it waits for the running operation, then flips consent; no new
restoration write can start after that because the worker rechecks consent
after acquiring.

**Wait bounds.** The service worker waits a short bounded time (seconds) and
treats `TimedOut` as "skip this scan, the app is busy"; it never spins. The
app's Apply waits longer (tens of seconds) and surfaces `TimedOut` as a
status line naming the service; it does not fall through to writing. The
per-batch cancellation token from the batch-safety step is the same token
passed to `AcquireAsync`, so cancelling Apply while waiting for the lease
returns `Cancelled` with nothing written.

**Recovery prerequisite, every acquisition.** `Acquired` and
`AcquiredAbandoned` both hand back a lease with `CanWrite` false. Before any
write the holder must:

1. Read the journal (step 3) for intents with no outcome. Each one is an
   uncertain write: read the live value again; a match is observed recovery,
   not proof the dead holder wrote it. Record a conflict entry that keeps the
   original intent. Never create an ordinary undo entry from it. An empty
   result (no open intents) is the normal case and completes in one read.
2. Treat any staged group marked `ReconciliationRequired` on the local queue
   as unresolved and leave it staged.
3. Call `MarkRecovered()`. Only then is `CanWrite` true, for this lease only.

`WasAbandoned` (outcome `AcquiredAbandoned`) goes into the log and the
journal's conflict record as a diagnostic. It changes nothing about the
steps above, and `Acquired` is not evidence that the previous holder
finished. Until the journal exists no code may call `MarkRecovered()`; a
lease acquired today must be treated as "do not write": dispose it, log,
and skip. `CanWrite` is the single gate every writer checks immediately
before its first write; a caller that writes while `CanWrite` is false has
a bug, not a policy.

**Refused and Faulted.** Neither is retried in a loop and neither falls back
to writing without the lease. `Refused` is a security event: log it with the
message (it names the owner or the offending ACE), show it in the Owner Mode
settings as "another program is holding the coordination lock", and keep
restoration off. `Faulted` is logged with its Win32 code and the operation is
skipped.

**Lifetime.** The lease is held across `await`s freely; the owner thread, not
the caller's thread, owns the kernel object. Dispose it in a `finally` or
`await using`. Never hold it across a UI prompt. Never take two leases in
one process for one operation: a second acquisition from the same process
times out like a foreign one (ownership is per thread, and each lease has its
own thread), and that is deliberate.

**Journal linkage.** Each intent written under the lease records the
`LeaseId`, the `OwnerLabel`, and `AcquiredAt`. Recovery groups intents by
`LeaseId`, so a dead holder's whole operation is one conflict record.

## Tests

- `tests/ThisIsMyPC.Core.Tests/Coordination`: `FakeMutationLease` and
  `FakeMutationLeaseProvider` (grant, exclusion while held, scripted results,
  abandoned grant as diagnostic, release without clearance leaves the next
  holder gated) and `MutationLeaseContractTests` (fresh and abandoned leases
  both start unable to write, `MarkRecovered` rules, dispose without
  clearance still releases, single release under concurrent `Dispose` and
  `DisposeAsync`, result factories and invariants, name validation, fake
  lifecycle). 37 tests, CI-safe.
- `tests/ThisIsMyPC.Security.Tests/Coordination/NamedMutexMutationLeaseTests`
  (`Category=Integration`; the Interop.Win32 test project the plan named does
  not exist, and Security.Tests already covers Interop.Win32). Every test
  uses `MutationLeaseNames.ForTest()`. Unelevated it verifies: pre-cancelled
  token, refusal of a pre-created object with an Everyone DACL, with an extra
  trustee, with a deny entry, of another kernel object type (error 6), refusal
  of a fresh object owned by a non-admin creator, argument validation. The
  `Elevated_*` tests return early unelevated (xunit 2.9.3 has no dynamic
  skip) and a sentinel test fails to say so. Elevated they verify: acquire
  gives a lease that needs clearance, `MarkRecovered` enables writing, the
  object is gone after release; a release without clearance leaves the next
  holder gated; a sole holder closing its handle while owning (a crash)
  destroys the object and the fresh mutex reads `Acquired`, not abandoned,
  and is still gated; second instance times out while held and acquires
  after release; zero-wait probe; same-provider second lease excluded;
  cancellation while held returns promptly; an abandoned holder (raw thread
  exits with the handle open) yields `AcquiredAbandoned` with the same gate,
  and the next holder reads `Acquired` and is still gated; a pre-created
  object with the exact DACL is accepted; the lease survives async
  continuations and disposal from another thread; `DisposeAsync` frees it
  for the next holder; and one real cross-process check: an elevated
  PowerShell child opens the test mutex and holds it, the provider times out
  against it, and killing the child while a test-side handle keeps the
  object alive yields `AcquiredAbandoned`, still gated. The pre-created
  Everyone-DACL object carries two entries so the trustee check, not the
  count check, is what refuses it. The in-thread abandonment and crash tests
  wait for the raw handle to be open before the keeper lease releases, so
  the object cannot vanish early. The cancel-in-the-same-instant path in the
  provider has no deterministic test; it is covered by reading the code.

Run the elevated set from an elevated prompt:

```
dotnet test tests/ThisIsMyPC.Security.Tests --configuration Release --filter "FullyQualifiedName~Coordination"
```

## Open items

- The elevated test set was not run in the session that wrote it (the worker
  process was not elevated). Run the command above elevated before wiring.
- The `Global\` namespace needs `SeCreateGlobalPrivilege`; the app and the
  service both have it (elevated, SYSTEM). A future non-elevated helper must
  not take this lease.
- `ReleaseMutex` failing after a granted lease is logged, not surfaced. When
  the journal exists, log it there too.
