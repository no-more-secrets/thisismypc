# Windows app removal with Arbitrary Code Guard

The October 12 Desktop Grand Pack test reported four failed removals: Clipchamp,
News, Weather, and Microsoft 365. The elevated helper used NativeAOT with
Arbitrary Code Guard enabled.

The native probe reproduced a `TypeInitializationException` from the WinRT
completion handler. Its inner exception reported that dynamic entrypoint
allocation was unsupported. `AsTask` registered the callback after Windows had
already started removal. This explains why a retry could find an app gone after
the first attempt reported failure.

Removal and deprovisioning now poll the native operation until it leaves
`Started`, read its final result, and close it. Cancellation remains effective
before deployment starts. After deployment starts, the helper waits for its
result so the action queue receives the actual outcome.

## Verification

- Four regression cases cover completion, failure, cancellation status, and
  status-read failure.
- A NativeAOT probe removed `Microsoft.BingNews_1.0.2.0_x64__8wekyb3d8bbwe`
  in the separate Windows 11 25H2 test VM, build 26200.6584.
- Deprovisioning returned success. A subsequent scan found no installed News
  package. Arbitrary Code Guard remained enabled.
- Evidence: `artifacts/diagnostics/restore-point-vm/appx-result.log` and
  `artifacts/diagnostics/appx-probe/`.

This probe does not establish complete Desktop Grand Pack acceptance, reinstall
acceptance, or compatibility across other Windows builds. The original guest
still requires the corrected administrator helper before repeating Apply.
