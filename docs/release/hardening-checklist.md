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

- **Arbitrary Code Guard: DONE.** Every NativeAOT App, Service, and Installer
  enables `ProcessDynamicCodePolicy` before Avalonia or host startup and fails closed
  if Windows does not confirm it. Avalonia.Win32 11.3.12 and SkiaSharp 2.88.9
  used managed delegate thunks that ACG blocks. Pinned local rebuilds replace
  those thunks with static unmanaged function pointers. A strict process
  creation test kept ACG active before resume and after the main window opened.
  The rebuilt Skia, HarfBuzz, and SQLite libraries load under the same strict policy.
  `tools/AcgLauncher` preserves this loader-time release test in the repository.
  The shipped self-enable path still starts at managed `Main`. Loader-time ACG
  needs a trusted launcher or machine policy as a separate hardening step.
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

## IPC and elevation boundary (audited against threat-model part 1, tm1)

- **Pipe ACL: OK.** `D:P(A;;GA;;;SY)(A;;GA;;;BA)` protected DACL (SYSTEM +
  Administrators only), FILE_FLAG_FIRST_PIPE_INSTANCE (anti-squatting,
  tm1 mitigation 1), PIPE_REJECT_REMOTE_CLIENTS, single instance
  (HardenedPipeFactory). Squatted names are logged critical and never served
  around.
- **Impersonation: OK.** The client connects with
  TokenImpersonationLevel.Identification (SECURITY_SQOS_PRESENT |
  SECURITY_IDENTIFICATION), exactly tm1 mitigation 2: a rogue server cannot
  use the client token.
- **Connecting-process identity: OK by design.** The kernel enforces the pipe
  DACL at open: only admin/SYSTEM tokens can connect at all. No PID-based
  checks (tm1 explicitly calls those spoofable); no server-side impersonation
  added (ImpersonateNamedPipeClient is itself attack surface the service does
  not need).
- **Input validation at the boundary: OK.** Length-prefixed frames with a hard
  1 MB cap enforced on read AND write, strict source-generated JSON (parse
  failure returns an error envelope, never an exception path), per-request
  nonce echoed and checked (replay guard), idle-session timeout, unknown types
  answered with Error. The GUI-to-service direction carries NO mutation
  commands (read-only status/drift queries); drift data flows into staged
  pending changes that a human reviews before apply.
- tm1 mitigation 3 (authenticated RPC ncacn_np + PKT_PRIVACY) remains the
  documented upgrade path if the envelope ever grows mutation commands
  (agent-interface chapter); the current read-only surface does not justify it.

## Build hygiene

- **Symbol stripping: OK.** `vpk pack` excludes `.pdb` by default (verified in
  the CLI reference); release packages ship no symbols. PDBs stay local for
  crash-log symbolication.
- **Reflection metadata: OK / deliberate.** NativeAOT trims unreachable
  metadata by definition; the two reflection-binding templates were converted
  to compiled bindings (zero IL trim warnings). Stack-trace metadata is
  deliberately KEPT (`StackTraceSupport` default): NLog crash logs need
  frames, and the metadata discloses nothing an open-source repo does not.

## Elevated content and child execution

- **Untrusted file parsing: DONE.** The elevated UI no longer resolves shortcut
  contents, reads PE version resources, inspects autorun signatures, or asks
  the shell to extract icons from target files. Icons, publishers, and
  descriptions stay unknown until this work moves to a non-elevated helper.
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

The main remaining architectural risk is the always-elevated Avalonia process.
ACG starts at managed Main, after the Windows image loader. A future split must
run the UI without elevation and expose a small authenticated mutation broker.
PPL is unavailable to a normal OV-signed desktop product.

## Verification (2026-09-06)

The non-elevated Release suite passed 2,359 tests. The numbered acg-04
NativeAOT build contains the App, Service, and installer launcher. All twelve
PE images passed the strengthened mitigation gate. The installer screenshot
suite confirmed that an outside-Program-Files path shows an error and disables
installation. Elevated loader-time checks passed for the App and Service.
ACG was active before each process resumed, remained active after startup, and
both processes stayed alive. A Debug NativeAOT installer, which skipped only
the release signature check, passed the same test and opened its main window.
The elevated security project also passed all 69 tests.

`ChildProcessGateTests` covers the winget launch gate directly, including a
live Integration case that resolves the real winget alias and verifies its
packaged executable as Microsoft-signed.
