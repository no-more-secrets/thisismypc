# Single-owner durable baseline

`SingleOwnerBaselineStore` holds chosen values for one primary Windows account. It is separate from the legacy observational `DriftBaselineStore`.
No production registration or automatic restoration changes in this batch. The legacy best-effort store must never authorize automatic restoration.

Version 1 binds the document to one primary SID. The caller must supply that same supported account SID on every open.
A mismatch, missing identity, malformed document, duplicate JSON field, duplicate target, unsupported value, or unsupported version throws.
A missing file returns no choices. The first explicit applied batch creates the owner binding. There is no account takeover or migration operation.
Entries contain canonical HKCU locations and typed expectations validated against the existing eleven-target DWORD catalog.
There is no per-entry account field or multi-account merge. Documents are limited to 1 MiB and the catalog's entry count.

`MachineBaselineStorage` implements the concrete trusted adapter in Interop.Win32.
It uses the existing consent directory checks and protected file writer: fixed local disk, held ancestry, no reparse points or hardlinks,
SYSTEM/Administrators ownership, and the exact protected SYSTEM/Administrators DACL.
Unsafe storage is refused without repair. The adapter uses a unique protected temporary file, Flush(true), and atomic write-through replacement.
It rereads the published document before success. A failed confirmation may mean publication occurred: callers must inhibit restoration until reread and reconciliation.
Failures before publication preserve the previous file. Corrupt trusted content remains evidence and cannot become an empty replacement baseline.
The adapter does not create or harden directories. Installation owns the protected data directory.

Read requires a matching held mutation lease and remains available during recovery. Writes require the recovered lease.
The caller must hold the lease until the synchronous operation returns and serialize operations within it.
All storage errors propagate. The future App caller must show persistence failure even when a system change already succeeded.
Journal import must not update chosen values because a restoration merely returned the system to an existing expectation.

Consent stays a separate machine-wide off/on record. Consent alone does not authorize any profile.
Integration must match the authenticated primary identity, the persisted baseline owner, valid consent, and loaded account evidence under the same lease.
SID syntax is internal addressing, not proof of account ownership. No profile enumeration or hive loading is introduced.
The crash gap between a user change and baseline publication must be inhibited by Step 4 before production restoration activates.

The next restore-loop batch must construct the journal and history repository only under verified protected data-directory access.
Their trust adapters remain deferred until that concrete caller exists; this batch adds no generic storage framework.

Coverage includes Core restart/update/mismatch/corruption/error tests and isolated elevated native tests for durable reopen,
unsafe ACLs, reparse ancestry, hardlinks, locked destinations, and corrupt-content preservation. Actual power loss is not simulated.
