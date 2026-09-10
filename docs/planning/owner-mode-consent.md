# Durable machine restoration consent

Historical foundation record. Statements about unwired production code describe the original checkpoint.
Current software integration and remaining live evaluation are recorded in the
[single-user plan](owner-mode-single-user-plan.md).

`IMachineConsentStore` stores explicit machine consent separately from service installation or running state.
`MachineConsentStore` is not registered in production. No service worker consumes this consent yet.
Missing, malformed, unsupported, inaccessible, or untrusted data never grants consent.

## Trust boundary

The default file is `owner-mode-consent.json` under the app's ProgramData directory.
The directory must already exist on a fixed local drive.
The store never creates directories, repairs ACLs, or adopts existing untrusted files.

Each directory component is opened without following its final reparse point.
The store rejects reparse points and holds every ancestor without write or delete sharing until the operation ends.
An existing incompatible handle causes refusal; there is no weaker fallback.
This keeps path components from being replaced or changed into reparse points during file access.

The immediate parent and consent file must be owned by SYSTEM or Administrators.
Both require a protected DACL with exactly two full-access allow entries, one for each trusted identity.
Directory entries must inherit to files and directories; file entries must have no inheritance flags.
Unknown ACE types, extra entries, invalid SID lengths, failed native reads, and duplicate identities are refused.
Existing consent must be a regular file with exactly one hard link.
Read authorization and document reads use the same file handle.

Ancestors above the immediate parent need not use the application's private DACL.
Their handles prevent write/delete access during the operation, and every component must be a normal directory.
The threat boundary excludes a malicious administrator, SYSTEM process, or kernel component.

## Explicit changes

Every consent change requires a held `IMutationLease` with the configured name.
Enabling also requires recovery. Disabling does not: corrupt journal data must not prevent durable opt-out.
This exception only writes consent off; it grants no other mutation and leaves the lease unrecovered.
Production defaults to `MutationLeaseNames.Production`; isolated tests supply private names.
The caller owns the lease lifetime and must not dispose it concurrently with a consent operation.
The store checks the lease before storage access and again before replacement.

The store verifies any existing destination before replacing it.
An explicit change can replace corrupt trusted content; it cannot replace an untrusted file.
A unique create-new temporary file receives its protected owner and DACL at creation.
The store verifies that handle, writes the document, and calls `FileStream.Flush(true)`.
It then uses same-directory `MoveFileExW` with replace-existing and write-through flags.
A final trusted read must confirm the requested state before success is returned.
No cross-volume copy or permission-repair fallback exists.

Any uncertain write result reports consent off to its caller.
A failed disable can leave the old enabled file unchanged; callers must stop restoration and surface the failed disable.
A failed call must never be presented as confirmed durable disable.
Unique temporary files are removed after failure; the destination is never deleted as cleanup.

## Document format

Version 1 contains exactly `version`, `enabled`, and `changedAtUtc`.
The timestamp must be UTC and cannot be in the future relative to the injected clock.
Duplicate or unknown properties, missing fields, wrong types, trailing data, and documents above 4096 bytes are rejected.
`enabled` must be an explicit JSON boolean. Service state and old settings do not imply consent.

## Verification and limits

Pure tests cover explicit values, malformed fields, duplicated keys, future dates, unsupported versions, and size limits.
Native integration tests use only isolated temporary directories and require elevation for Administrators ownership.
They cover recovered lease requirements, replacement, weak ACL refusal, corrupt content, reparse ancestry, and hard links.

Native tests do not change production ProgramData consent or activate restoration.
Power-loss injection, hostile concurrent filesystem stress, and service integration remain unverified.
Use the separate mutation recovery protocol before enabling consent. Disabling still requires a matching held lease.

The durability calls follow Microsoft's documented [MoveFileExW semantics](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw).
Handle security uses [GetSecurityInfo](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getsecurityinfo).