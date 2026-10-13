# Automatic System Restore protection

Restore-point creation first calls `SRSetRestorePointW`. If Windows reports
`ERROR_SERVICE_DISABLED` (1058), ThisIsMyPC enables protection on the Windows
system drive and retries creation once. This covers manual restore points and
restore points requested before bulk Apply through the same Broker service.

Protection stays enabled after creation. Other drives are not enabled.
Administrator `DisableSR` or `DisableConfig` policy blocks enablement. ThisIsMyPC
reads these policies and does not change them. An enable or retry failure remains
a failure. Missing Windows exports also fail without claiming a restore point.

## Native evidence

REA inspected `C:\Windows\System32\srclient.dll` on Windows build 26300.
SHA-256: `6d79fe1f2cb9d4133e86e1d3e9526c99468398c0e0bef47ae7526fc904137e13`.

The exported `EnableSR` function at `0x180003b20` accepts a UTF-16 drive path.
Its observed implementation initializes COM, calls the internal volume-enable
path, translates the result into a Win32 status, and returns zero on success.
The internal volume-enable path uses native COM calls. ThisIsMyPC imports the
export directly with source-generated marshalling and System32-only DLL search.
It does not use PowerShell, WMI, or managed COM marshalling for enablement.

This is an undocumented export. The signature was inferred from the shipped
binary, so existence alone does not establish compatibility with every supported
Windows build. Real creation in a disposable VM verifies the imported signature.

The Windows SDK SRRestorePtApi.h declares both structures with packing of one byte.
STATEMGRSTATUS is 12 bytes, with the sequence number at offset four.
The previous default packing read the sequence at offset eight.
ThisIsMyPC now uses the SDK layout and rejects results without a positive sequence.

## Verification

Thirteen CI-safe regression cases cover existing protection, unrelated failures,
enable failure, policy refusal, successful retry, and retry failure. They do not
change the test machine. Read-only diagnostic cases resolve both Windows exports.
The CI-safe suite passed 3,842 tests. The native probe published successfully.

On Windows 11 Pro build 26200.6584, the native probe enabled disabled protection and created restore point 12. Windows listed the same description and sequence. The probe also enabled Arbitrary Code Guard and the Broker DLL search policy. DisableSR policy refused creation, preserved the policy value, and added no point. Grand Pack Apply and undo, installer updates,
and browser-download SmartScreen behavior remain separate acceptance checks.
