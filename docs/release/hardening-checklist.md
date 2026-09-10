# Release hardening checklist (Sam's final list, worked 2026-08-31)

Status legend: DONE (implemented + verified), IMPL (implemented this session,
build/test verification still pending, see the note at the bottom), OK (already
satisfied, verified this session), N/A (does not apply, reason given).

## Compile-time mitigations

- **Control Flow Guard: DONE.** The CoreCLR apphost ships CFG by default
  (dumpbin verified). The NativeAOT toolchain does NOT: the probe exe had zero
  guarded functions. `<ControlFlowGuard>Guard</ControlFlowGuard>` now rides the
  AotPublish gate in ThisIsMyPC.App.csproj; rebuilt and dumpbin-verified
  (Control Flow Guard characteristic, ~83k guarded functions).
- **DEP: OK.** NX compatible bit verified on both CoreCLR and AOT exes.
- **ASLR: OK.** Dynamic base + High Entropy VA verified on both.
- **EH continuation guard: DONE.** Absent from the unguarded AOT probe;
  enabling CFG brought the Guard EH Continuation Table with it
  (dumpbin-verified). Present on the CoreCLR apphost by default.
- **CET / shadow stack: OK.** "CET compatible" extended characteristic verified
  on both exes (default since .NET 9 era toolchains). Hardware without shadow
  stacks ignores the bit; nothing further to do.
- **Native dependency mitigations: DONE.** Pinned source builds replace the
  Windows x64 Skia, HarfBuzz, and SQLite libraries. Each has CFG, CET, ASLR,
  high-entropy VA, DEP, `/GS`, and table-based unwinding. Export lists match
  the official packages exactly. The custom Skia build removes unused Vulkan
  support. The remaining ANGLE library already has every checked mitigation.

Re-verify any release binary with:
`dumpbin /headers /loadconfig <exe>` (VS MSVC tools). Expect: Dynamic base,
High Entropy VA, NX compatible, Control Flow Guard, CET compatible,
EH Continuation table present.

## Process and loading hardening

- **Arbitrary Code Guard: DONE.** Every release App, Broker, Service, and Installer
  enables `ProcessDynamicCodePolicy` before Avalonia or host startup and fails closed
  if Windows does not confirm it. Avalonia.Win32 11.3.12 and SkiaSharp 2.88.9
  used managed delegate thunks that ACG blocks. Pinned local rebuilds replace
  the four callback tables and nine operation callbacks with static unmanaged
  function pointers. A strict process
  creation test kept ACG active before resume and after the main window opened.
  The rebuilt Skia, HarfBuzz, and SQLite libraries load under the same strict policy.
  `tools/AcgLauncher` also rejects a created window when its captured frame remains black.
  ACG App and Installer builds use software rendering because ANGLE presented black frames.
  The shipped self-enable path still starts at managed `Main`. Loader-time ACG
  needs a trusted launcher or machine policy as a separate hardening step.
  `AotPublish=true` alone omits ACG for fast unsigned diagnostics.
  `DynamicCodeGuard=true` enables ACG without requiring code signing.
- **Code Integrity Guard: DONE.** NativeAOT App startup verifies the complete
  packaged native dependency set against baked canonical SHA-256 values. The
  canonical hash excludes only a valid terminal Authenticode certificate table.
  Startup then requires WinVerifyTrust and the exact No More Secrets, LLC signer.
  It maps those four fixed files by absolute path and confirms each loaded path.
  A pre-CIG inventory permits only the App, those four files, System32, and WinSxS.
  Startup then enables the Microsoft-only process signature policy before Avalonia,
  logging, IPC, or user input. The process stops if any check fails. A live probe kept
  ACG and CIG active, scanned a physical display, and rejected an unsigned DLL.
  Process-creation CIG cannot admit our OV-signed native libraries. The current
  managed-entry policy leaves a small pre-entry injection window. The module
  inventory detects a mapped side-loaded image before policy activation. Ordinary NativeAOT builds omit
  ACG and CIG, so unsigned Debug builds can use normal NativeAOT behavior.
  Unsigned ACG diagnostics pass `DynamicCodeGuard=true`. The release script
  enables ACG and CIG explicitly. CIG can reject optional third-party Winsock
  providers such as Apple Bonjour. The App suppresses Windows critical-error
  dialogs so Winsock can skip the rejected provider without blocking startup.
- **Safe DLL search: DONE.** New `DllSearchHardening.Apply()`
  (SetDefaultDllDirectories: SYSTEM32 + application dir only, PATH and CWD
  removed process-wide) called before framework startup in the App, Service,
  and Installer. Complements the existing per-assembly
  `DefaultDllImportSearchPaths(System32)` attributes (NFR30), which cannot
  reach delay-loaded or dependency-pulled DLLs.
- **Safe DLL search failure: DONE.** App, Service, and Installer now stop when
  SetDefaultDllDirectories fails. The portable release installer excludes its
  download directory and permits only System32. Its Avalonia native libraries
  load by absolute path from protected ProgramData storage.
- **Delay-load hardening: covered by the above.** Delay-load thunks resolve
  through LoadLibrary, which SetDefaultDllDirectories constrains. No custom
  delay-load handlers exist in the codebase.
- **Child-process signature verification: IMPL.**
  Inventory of every launch site:
  - `winget.exe` (WingetService): THE risk case; the app-execution alias lives
    under the user-writable profile. Now: full-path resolution only (no bare
    names handed to CreateProcess), the alias reparse point is resolved to the
    real packaged exe (`AppExecutionAlias.ResolveTarget`, APPEXECLINK), and
    that PE must pass WinVerifyTrust with a "Microsoft Corporation" signer
    subject (`AuthenticodeVerifier.VerifyTrusted`) before launch. Verified
    once per path per process. Residual alias-swap TOCTOU accepted and
    documented in code.
  - `explorer.exe` (ExplorerRestartService): full Windows-directory path
    already; OS-protected location; no signature gate needed.
  - Releases page (MainWindowViewModel): a URL via ShellExecute; the browser
    launch is the shell's, not ours. N/A.
  - `Update.exe` (Velopack internal): lives in the install directory
    (Program Files at release, admin-only) and the update package content is
    GPG-manifest-verified before apply. Velopack launches it internally; no
    interception point, covered by location + manifest.
  - Installers/uninstallers: none; the MSI is WiX/Velopack-run, not launched
    by the app.

## IPC and elevation boundary

- **Broker split: DONE.** The Avalonia UI runs at medium integrity. A small,
  Avalonia-free NativeAOT Broker starts through `runas` for each approved batch.
- **Broker pipe: DONE.** Each session uses a random first-instance local pipe.
  Its protected DACL names only the UI account, Administrators, and SYSTEM.
  Both processes bind the connection to the expected peer PID.
- **Broker identity: DONE.** Release builds require the installed UI and Broker
  paths under Program Files. Authenticode must identify No More Secrets, LLC.
- **Broker consent: DONE.** The elevated native confirmation lists every change,
  action, target, and enforcement target. It paginates large batches and defaults
  to Cancel. Commands must exactly match the approved session.
- **Broker parsing: DONE.** Frames have a 1 MB cap. Text rejects control
  characters and oversized values. Operation count, enforcement count, session
  opening, idle time, and execution time are bounded.
- **Service pipe: DONE.** Authenticated local users can read service status and
  drift. Enable and pause controls require an elevated connecting process.
  Remote clients and second pipe instances remain rejected.

## Build hygiene

- **Symbol stripping: OK.** `vpk pack` excludes `.pdb` by default (verified in
  the CLI reference); release packages ship no symbols. PDBs stay local for
  crash-log symbolication.
- **Reflection metadata: MOSTLY DONE.** NativeAOT trims unreachable metadata.
  One tab-template `ReflectionBinding` remains and produces one IL2026 warning.
  Stack-trace metadata stays enabled because NLog crash logs need frames. The
  metadata discloses nothing absent from the open-source repository.

## Elevated content and child execution

- **Untrusted file parsing: DONE.** The medium-integrity UI performs discovery
  and display parsing. The elevated Broker receives bounded typed requests only.
- **Third-party shell code: DONE.** Context-menu discovery reads registry
  metadata only. It never creates or calls a registered in-process shell
  extension. Surface classification falls back conservatively.
- **Startup folder containment: DONE.** Startup file reads, restores, deletes,
  and moves accept only direct children of the two Windows Startup folders and
  their AutorunsDisabled folders. Cross-scope moves, traversal, nested paths,
  and reparse points are refused.
- **Portable installer cache: DONE.** Embedded native libraries are compared by
  SHA-256. A same-length poisoned cache entry is replaced. Private extraction
  paths reject reparse points.
- **Installer child execution: DONE.** Per-machine detection ignores HKCU
  uninstall records. Install, update, launch, and uninstall paths must be under
  Program Files. Update.exe must occupy the exact expected path and carry a
  trusted No More Secrets, LLC signature before execution.
- **Elevation fallback: DONE.** Failure to obtain an interactive user token no
  longer starts the requested process with the app's elevated token.
- **Protected app directory: DONE.** Release builds stop before framework
  startup outside Program Files. Canonical path checks reject traversal and
  prefix lookalikes.

The always-elevated Avalonia risk is removed. A compromised UI can request a
malicious target, but it cannot apply one silently. The elevated confirmation
shows the exact target before the Broker accepts the session. PPL remains
unavailable to a normal OV-signed desktop product.

## Verification (2026-09-06)

The Release build and all 2,455 CI-safe tests passed. Numbered build
`0.0.1-broker.1` contains NativeAOT App, Broker, Service, and installer
executables. All four passed the ASLR, high-entropy VA, DEP, CFG, /GS, CET,
SEH, and W^X release gate. The package is unsigned because no eSigner inputs
were present. Live Broker elevation, signed identity checks, and a per-machine
update from the medium-integrity UI remain untested.

`ChildProcessGateTests` covers the winget launch gate directly, including a
live Integration case that resolves the real winget alias and verifies its
packaged executable as Microsoft-signed.
