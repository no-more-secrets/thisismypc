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

Re-verify any release binary with:
`dumpbin /headers /loadconfig <exe>` (VS MSVC tools). Expect: Dynamic base,
High Entropy VA, NX compatible, Control Flow Guard, CET compatible,
EH Continuation table present.

## Process and loading hardening

- **Safe DLL search: IMPL.** New `DllSearchHardening.Apply()`
  (SetDefaultDllDirectories: SYSTEM32 + application dir only, PATH and CWD
  removed process-wide) called first thing in both entry points (App
  Program.Main, Service Program). Complements the existing per-assembly
  `DefaultDllImportSearchPaths(System32)` attributes (NFR30), which cannot
  reach delay-loaded or dependency-pulled DLLs.
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

## Verification (paid 2026-08-31, follow-up session)

Every IMPL item above is now built, tested, and committed: solution build
clean, full CI suite green (1,382 tests), Diagnostic UI walkthrough green.
`ChildProcessGateTests` covers the new gate directly, including a live
Integration case that resolves the real winget alias and verifies its
packaged executable as Microsoft-signed: the exact path the launch gate
takes in production.
