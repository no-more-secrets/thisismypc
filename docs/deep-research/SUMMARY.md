---
author: Claude Opus 4.6 (synthesis of 7 Gemini 3.1 Pro Deep Research documents)
date: 2026-03-08
source_documents:
  - threat-modeling-research-part1.md (tm1) — 21 pages
  - threat-modeling-research-part2.md (tm2) — 23 pages
  - windows-kernel-driver-security-research.md (kd) — 21 pages
  - windows11-context-menu-research-part1.md (cm) — 20 pages
  - windows11-context-menu-research-part2.md (cm2) — 16 pages
  - windows11-control-surface-research.md (cs) — 34 pages
  - nativeaot-runtime-integrity-research.md (ri) — 17 pages
citation_format: "[abbreviation:line(s)]"
---

# Deep Research Synthesis: Windows 11 Architecture and ThisIsMyPC Security Posture

This document synthesizes 152 pages of deep research into a unified reference for the ThisIsMyPC project. It covers the Windows 11 platform constraints, security architecture, and implementation implications across five domains: kernel security, IPC hardening, shell integration, configuration surface management, and user-mode runtime integrity.

## 1. The Windows 11 Trust Model

Windows 11 has fundamentally restructured the kernel trust hierarchy. The hypervisor (Ring -1) now sits above the OS kernel (Ring 0) as the ultimate arbiter of execution privileges. [kd:10-14] [tm1:10]

### 1.1 VBS/HVCI Architecture

- **Virtualization-Based Security (VBS)** isolates the primary OS into Virtual Trust Level 0 (VTL 0), with a Secure Kernel in VTL 1 [kd:156]
- **HVCI** enforces strict Write-XOR-Execute (W^X) protections via Extended Page Tables (EPT) [kd:158] [tm2:151-152]
- Drivers cannot allocate memory that is simultaneously writable and executable [kd:158-160]
- Raw physical memory mapping (`MmMapIoSpace`) and unconstrained `IN`/`OUT` port instructions trigger immediate hypervisor interceptions and BSODs [kd:160] [tm2:152-153]
- Legacy tools like WinRing0 are fundamentally incompatible; Microsoft blocklisted them as `HackTool:Win32/Winring0` (CVE-2020-14979) [kd:84-86]
- Aggressive I/O port polling of undocumented embedded controller ports can cause Windows 11 to silently disable HVCI on reboot [kd:166]

### 1.2 Driver Signing Requirements

- All production kernel drivers must be signed through the Microsoft Hardware Dev Center using an EV Certificate [kd:22-26]
- **Attestation Signing**: No HLK tests required, cannot distribute via Windows Update, Windows 10/11 desktop only [kd:32-40]
- **WHQL Certification**: Requires HLK test passage, eligible for Windows Update distribution, supports all platforms [kd:32-42]
- PawnIO must use Attestation signing at minimum; test signing requires disabling Secure Boot (unacceptable for production) [kd:44]
- Driver Signature Enforcement (DSE) blocks any `.sys` file without a valid Microsoft signature [kd:46-48]

### 1.3 Kernel DMA Protection

- IOMMU blocks unauthorized hot-plugged peripherals (PCIe, Thunderbolt, USB4) from performing DMA [kd:170-174] [tm1:24]
- Hardware drivers must use official DMA abstraction APIs (`AllocateCommonBuffer`), never manual scatter-gather lists [kd:174] [tm1:24]
- SMBus/I2C communication should use the native SpbCx framework, not manual port bit-banging [kd:268] [tm2:162]

## 2. Microsoft's Enforcement Drivers

Windows 11 ships enforcement drivers that actively resist user configuration changes — even from Administrator/SYSTEM processes. Understanding these is critical for ThisIsMyPC's enforcement layer. [kd:50-52]

### 2.1 UserChoice Protection Driver (ucpd.sys)

- Deployed via KB5034765 to block unauthorized changes to default app associations (`http`, `https`, `.pdf`) [kd:56]
- Uses bipartite logic: hardcoded deny list (`reg.exe`, `powershell.exe`, `cmd.exe`, `rundll32.exe`, `WmiPrvSE.exe`) + Microsoft-signature whitelist (`IsMicrosoftSignedFile`) [kd:58]
- Marked `NOT_STOPPABLE` — no unload routine, cannot be detached via `fltmc` at runtime [kd:60]
- Persistence via `UCPDMgr.exe` scheduled task (`\Microsoft\Windows\AppxDeploymentClient\UCPD velocity`) that re-enables the driver on reboot even if manually disabled [kd:60]
- **Implication**: ThisIsMyPC must disable both the driver service and the scheduled task to manage file associations [cs:142-144]

### 2.2 Windows Defender Filter Driver (wdfilter.sys)

- Uses `ObRegisterCallbacks` to strip sensitive handle access rights (`PROCESS_VM_WRITE`, `PROCESS_VM_OPERATION`, `PROCESS_CREATE_THREAD`) from unauthorized processes [kd:64-68]
- Callback `MpObHandleOpenProcessCallback` evaluates requested access rights; injection limited to processes flagged as `ExcludedProcess`, `MpServiceSidProcess`, or `FriendlyProcess` [kd:68]
- Registry callbacks block modifications to `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender` [kd:70]
- Tamper Protection actively monitors and reverts unauthorized Defender registry changes [cs:66-68]
- The `DisableAntiSpyware` key is actively ignored by the engine unless a validated third-party AV registers via Security Center [cs:66]
- **Implication**: ThisIsMyPC's registry callbacks must never target Defender hives; doing so triggers heuristic rootkit detection [tm2:168-172]

### 2.3 GPCache Sync Layer

- Windows Update policies are duplicated into `HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache` by a scheduled task using `updatepolicy.dll` [cs:43-45]
- The Update Orchestrator reads the cached values, not the standard policy keys [cs:45]
- Modifying standard policy hives without clearing/syncing GPCache will silently fail — this is the primary reason users report update deferrals mysteriously reverting [cs:45-46]
- **Implication**: ThisIsMyPC must synchronize both the policy keys and the GPCache to make update settings stick [cs:295-296]
- **Open question — sync timing**: The source research does not document whether the Update Orchestrator uses registry change notifications or polls on a schedule. If it polls, there is a race window between ThisIsMyPC writing the GPCache and the Orchestrator's next read cycle. Investigation is needed to determine whether restarting `UsoSvc` forces an immediate re-read. This matters for UX: if a user toggles "disable auto-updates" and the setting doesn't take effect for hours, it undermines trust in the tool.

## 3. Attack Surface Analysis: IPC and Kernel Boundaries

### 3.1 Named Pipe Vulnerabilities

Three attack classes target the GUI-to-Service IPC channel:

| Attack | Mechanism | Precedent | Source |
|---|---|---|---|
| **Pipe Squatting** | Unprivileged user creates the pipe before the service, retaining permissive ACLs; Object Manager ignores the service's SDDL | CVE-2021-1733 (PsExec) | [tm1:68-72] |
| **Token Impersonation** | Attacker forces SYSTEM to connect to a malicious pipe via `ImpersonateNamedPipeClient`, steals token, duplicates via `DuplicateTokenEx` with `TokenPrimary`, spawns SYSTEM process | PrintSpoofer, JuicyPotato, GodPotato | [tm1:56-64] |
| **MITM/Relay** | DLL injected into trusted GUI process hooks `ReadFile`/`WriteFile` via inline API hooking (Detours); bypasses both SDDL and PID validation | pipetap proxy | [tm1:74-78] |

**Required Mitigations:** [tm1:80-89]
1. `FILE_FLAG_FIRST_PIPE_INSTANCE` — prevents squatting; OS fails creation if pipe name already exists [tm1:72, 84]
2. `SECURITY_SQOS_PRESENT` + `SECURITY_IDENTIFICATION` — client restricts token to identification level only, prohibiting impersonation/delegation [tm1:86]
3. Authenticated RPC (`ncacn_np` with `RPC_C_AUTHN_LEVEL_PKT_PRIVACY`) using Kerberos/NTLMv2 mutual auth via SPNs + connection nonces to defeat replay [tm1:88]

### 3.2 IOCTL and Device Interface Vulnerabilities

| Vulnerability | Mechanism | Precedent | Source |
|---|---|---|---|
| **Namespace Traversal** | Omitting `FILE_DEVICE_SECURE_OPEN` lets attackers open `\Device\Name\BypassString`; I/O Manager skips SDDL evaluation for trailing path | CyberArk WDM research | [tm1:96-104] |
| **Buffer Overflow** | Missing `ProbeForRead`/`ProbeForWrite`; `PreviousMode` check absent; attacker supplies kernel address in IOCTL field for arbitrary write | CVE-2023-21768 (afd.sys) | [tm1:108-112] |
| **Arbitrary Process Kill** | IOCTL handler (`0x800024b4`) accepts PID, calls `ZwTerminateProcess` without caller ACL validation | CVE-2024-51324 (BdApiUtil.sys) | [tm2:66-68] |
| **Token Privilege Manipulation** | Vulnerable driver maps `_EPROCESS` via `MmMapIoSpace`, attacker flips `_SEP_TOKEN_PRIVILEGES` to grant `SeDebugPrivilege` + `SeLoadDriverPrivilege` | WinRing0 (CVE-2020-14979) | [kd:231-233] |

**Required Mitigations:**
1. `IoCreateDeviceSecure` with SDDL `D:P(A;;GA;;;SY)(A;;GA;;;BA)` + `FILE_DEVICE_SECURE_OPEN` [tm1:100-104] [kd:260-262]
2. `METHOD_BUFFERED` only (never `METHOD_NEITHER`); I/O Manager copies user data to non-paged pool safely [tm1:114]
3. Strict `InputBufferLength`/`OutputBufferLength` validation + `ProbeForRead`/`ProbeForWrite` on every embedded pointer [tm1:114] [kd:106]

### 3.3 TOCTOU and Hard Link Attacks

- **ASUS Armoury Crate** (CVE-2025-3464): AsIO3.sys validated caller via SHA-256 hash of backing executable. Attacker created hard link to malicious binary, launched and suspended it, then swapped the hard link to point to the legitimate signed binary. Driver hashed the swapped file, passed validation, and granted the malicious process a handle to `\Device\Asusgio3`. [tm1:40-44]
- **Razer Synapse** (CVE-2022-47631): Installer allowed unprivileged user to pre-create `%PROGRAMDATA%\Razer\Synapse3\Service\bin`. Attacker planted malicious DLL, exploited race condition between service's validation pass and DLL mapping. SYSTEM service loaded attacker's DLL. [tm1:46-48]
- **Razer Synapse** (CVE-2025-27811): SYSTEM-level `razer_elevation_service.exe` exposed a vulnerable COM interface; unprivileged attacker obtained reference and triggered arbitrary elevated operations. [tm1:46]
- **Lesson**: Never rely on PID allowlisting or user-space file hashing for authorization. Use cryptographic mutual authentication and locked installation directories (`C:\Program Files\`). [tm1:50]

## 4. Bytecode Interpreter Security (PawnPP)

PawnIO replaces WinRing0 by using a bytecode interpreter instead of exposing raw hardware access. [kd:88-94] This is architecturally sound but introduces its own risks.

### 4.1 The CrowdStrike Warning

The July 2024 CrowdStrike BSOD affected millions of systems because: [kd:140-148] [tm2:74-77]
- `CSagent.sys` had a kernel-mode content interpreter for evaluating "Rapid Response Content" via "Channel File 291" [kd:144]
- The compiled C++ parser was hardcoded to expect exactly 20 input parameter fields [tm1:120] [tm2:76]
- An automated update supplied a template with 21 fields [kd:144]
- Out-of-bounds read caused `PAGE_FAULT_IN_NONPAGED_AREA` in a boot-start driver → infinite BSOD boot loop requiring manual Safe Mode/WinRE remediation [kd:146]

**Core lesson**: Never parse complex, unverified data structures directly in the kernel. Parsing logic should reside in user-mode services, passing only sanitized, bounded structs to the kernel. [kd:148]

### 4.2 Required PawnPP Architecture (eBPF Model)

Drawing from the Linux eBPF verifier architecture: [tm1:122-123]

1. **User-Space Cryptographic Validation**: Session 0 service verifies bytecode signature (developer's private key) before transmitting to kernel via IOCTL [tm1:126] [tm2:80]
2. **In-Kernel Static Verifier**: Disassembles bytecode, constructs Control Flow Graph (CFG), proves DAG or strictly bounded loops, type-checks all 11 virtual registers, enforces memory bounds on every read/write within the 512-byte stack [tm1:122-128] [tm2:82]
3. **Structured Exception Handling**: `__try`/`__except` around the interpreter execution loop — graceful driver error code returned to user-space, never a kernel panic [tm1:130] [tm2:84]
4. **Hardcoded Public Key**: Kernel embeds public key and rejects any unsigned/tampered bytecode outright [tm2:80]

## 5. Callback Weaponization Risks

### 5.1 CmRegisterCallbackEx

- Allows kernel drivers to intercept and block registry operations system-wide via `CM_CALLBACK_CONTEXT_BLOCK` inserted into the `CmpCallBackVector` doubly linked list [tm1:134]
- Callback receives `REG_XXX_KEY_INFORMATION` structure; driver can inspect access rights, modify data in-flight, or return `STATUS_ACCESS_DENIED` [tm1:134] [kd:200-204]
- Legitimate use: protecting ThisIsMyPC's own configuration keys from Update Orchestrator (`UsoClient.exe`) [tm1:134] [kd:204]
- **Weaponization risk**: Mustang Panda APT uses CmRegisterCallbackEx via the "Hidden" rootkit project to hide malware in `HKLM\...\Run` and `...\Services` hives — spoofing `STATUS_OBJECT_NAME_NOT_FOUND` on queries and blocking deletions with `STATUS_ACCESS_DENIED` [tm1:136-138]
- If ThisIsMyPC accepts dynamic target lists from user-space, a compromised service could instruct the driver to protect attacker's persistence keys or blind EDR tools [tm1:140]

**Mitigations:** [tm1:142-148]
- Eradicate dynamic, unverified target lists from user-space [tm1:144]
- Statically compile protected keys into the EV-signed `.sys` binary, or require cryptographic signature validation (hardcoded public key) of any config file [tm1:146]
- Validate calling process identity via `PsGetCurrentProcessId` → EPROCESS block; permit modifications only from the cryptographically verified PID of the ThisIsMyPC Session 0 service (ucpd.sys model) [tm1:148]

### 5.2 File System Minifilter (FltRegisterFilter)

- **Altitude manipulation**: Filter Manager uses altitude integers to order minifilters; attacker deploys a higher-altitude filter to intercept and complete IRPs before ThisIsMyPC's filter sees them [tm1:156]
- **Unload attacks**: `fltmc unload <FilterName>` forcefully detaches the filter at runtime, even from SYSTEM [tm1:158]
- **Reparse point TOCTOU**: Attacker manipulates NTFS symlink between filter's metadata check and actual kernel memory use, causing Use-After-Free. Attacker heap-sprays to reallocate freed memory, achieves arbitrary read/write (CVE-2025-62221 in cldflt.sys) [tm1:164-166]

**Mitigations:** [tm1:158-168]
- Set `FilterUnloadCallback` to NULL in `FLT_REGISTRATION` — driver becomes NOT_STOPPABLE; Filter Manager rejects all unload attempts including from SYSTEM via fltmc [tm1:160]
- Resolve all reparse points via `FltGetFileNameInformation` with `FLT_FILE_NAME_NORMALIZED` + `FLT_FILE_NAME_QUERY_DEFAULT` before rendering access decisions [tm1:168]
- Track object lifecycles across pre/post-operation callbacks to prevent UAF [tm1:168]

## 6. Supply Chain and Distribution Security

### 6.1 Known Attack Patterns

| Attack | Technique | Duration Undetected | Source |
|---|---|---|---|
| SolarWinds SUNBURST | SUNSPOT malware monitored `MSBuild.exe`, injected backdoor source into memory during compilation; resulting DLL was legitimately signed | ~9 months | [tm2:40-43] |
| Codecov Bash Uploader | Attacker extracted GCS HMAC key from Docker misconfiguration, modified the hosted bash script to exfiltrate env vars (AWS keys, GitHub PATs) via injected `curl` | ~2 months | [tm2:28-34] |
| XZ Utils (CVE-2024-3094) | "Jia Tan" gained maintainer access via multi-year social engineering; injected malicious M4 macro only in release tarballs (not git), used IFUNC to hook `RSA_public_decrypt` in liblzma, achieving pre-auth RCE via SSH on Debian/RPM systems | ~2 years | [tm2:44-45] |
| Gentoo GitHub | Admin account compromised via weak password + no 2FA; force-pushed `rm -rf` into ebuild scripts; detected via email notifications when devs were locked out | 5 days | [tm2:24-26] |
| tj-actions/changed-files (CVE-2025-30066) | Compromised GitHub Action used to steal repository secrets from CI pipelines | N/A | [tm2:36] |

### 6.2 Required Mitigations

1. **Authenticode Signing**: Every `.exe` binary and Velopack delta `.nupkg` must be signed via EV certificate; `UpdateManager` must reject unsigned packages [tm2:52]
2. **Out-of-Band GPG Verification**: Detached `.sig` file signed by offline private key (held exclusively by lead developer); public key hardcoded in NativeAOT binary; even if GitHub + EV cert are both compromised, attacker cannot forge the GPG signature [tm2:54]
3. **Reproducible Builds**: Containerized CI/CD with frozen SDK/toolchain (Signal model) so community can compile source and verify bit-for-bit identical output [tm2:50]
4. **GitHub Action Security**: Pin all CI actions to commit SHA, not tags [tm2:36]
5. **Checksum Insufficiency**: SHA-256 checksums hosted alongside binaries prove nothing if both are compromised — they verify integrity, not origin [tm2:48]

## 7. NativeAOT-Specific Security

### 7.1 DLL Sideloading (MITRE T1574.001)

- .NET 10 NativeAOT changed behavior: single-file apps no longer auto-add the app directory to `NATIVE_DLL_SEARCH_DIRECTORIES` [tm2:96]
- But if P/Invoke bindings use default search behavior without explicit `DllImportSearchPath` annotation, the app directory may still be probed for unmanaged libraries [tm2:96-98]
- Attack scenario: attacker plants malicious `setupapi.dll` next to `ThisIsMyPC.exe` in a user-writable directory (`%USERPROFILE%\Downloads`); user clicks UAC "Yes" on the legitimate prompt; attacker's DLL loads with admin privileges [tm2:98]
- **Mandatory**: All CsWin32 P/Invoke bindings must use `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` [tm2:100]
- Application must reside in `C:\Program Files\` (admin-only write access), never user-writable directories [tm1:50]

### 7.2 User-Mode Runtime Integrity (CFG + ACG + CIG)

Dedicated research confirms that .NET 10 NativeAOT is fully compatible with all three Windows 11 user-mode exploit mitigations, closing the gap between kernel-side protections and in-process defense. [ri:§1-7]

**Control Flow Guard (CFG):**
- NativeAOT fully supports CFG via `<ControlFlowGuard>Guard</ControlFlowGuard>` in `.csproj` [ri:§2.1]
- The ILCompiler/RyuJIT pipeline accurately enumerates all valid indirect call targets and populates the GFIDS table in the PE load configuration directory — no false-positive terminations [ri:§2.2]
- CsWin32 P/Invoke boundaries are statically analyzed; `[UnmanagedCallersOnly]` callbacks are automatically registered as valid CFG targets [ri:§2.3]
- CET Shadow Stack (`CetCompat`) is enabled by default, providing hardware-backed ROP mitigation alongside CFG [ri:§2.1]
- Performance overhead: <1-2% CPU, imperceptible to end users [ri:§6.2]

**Arbitrary Code Guard (ACG):**
- NativeAOT eliminates the JIT compiler entirely, making ACG (`ProcessDynamicCodePolicy`) safe to enable — GC, exception handling, and interop thunks are all statically compiled [ri:§3.1]
- `System.Reflection.Emit` and unbounded generics are disabled under NativeAOT; ACG enforcement will not crash the app [ri:§3.1]
- ACG has **zero execution-time overhead** since NativeAOT never calls `VirtualAlloc` for executable memory [ri:§6.2]
- **WinUI 3 constraint**: Must use compiled bindings (`{x:Bind}` not `{Binding}`); Windows App SDK 1.6+ supports NativeAOT paths [ri:§3.2]
- **WebView2**: If used, its JIT runs in an isolated out-of-process sandbox — host process ACG is unaffected [ri:§3.2]

**Code Integrity Guard (CIG):**
- NativeAOT bundles all managed dependencies into a single native binary — CIG enforcement requires signing only the main `.exe` plus Microsoft-signed WinUI 3 DLLs [ri:§4.1]
- Blocks all unsigned DLL injection (classic, reflective, `LoadLibrary`-based) at the kernel memory manager level [ri:§4.2]
- Side effect: GPU overlays (RivaTuner, NVIDIA), AV hooks, and accessibility injectors will silently fail [ri:§4.2]
- For required unsigned third-party DLLs: use WDAC Supplemental Policies with SHA-256 hash allowlisting — `SetProcessMitigationPolicy` does not support exceptions [ri:§4.3]

**The "Already In-Process" Attack Chain:**
ObRegisterCallbacks protects against external handle acquisition but provides no defense against an attacker who has already achieved code execution within the process (e.g., via BYOVD or pre-initialization DLL injection). The CFG+ACG+CIG triad creates layered in-process defense: [ri:§5.1]
1. ACG prevents `VirtualAlloc`/`VirtualProtect` for new executable regions or code modification
2. Attacker is forced into ROP/JOP using existing gadgets
3. CFG bitmap validation detects invalid indirect call targets and terminates the process (`STATUS_STACK_BUFFER_OVERRUN`)

### 7.3 Enforcement Timing: IFEO over SetProcessMitigationPolicy

A critical finding: calling `SetProcessMitigationPolicy` in `Main()` leaves a TOCTOU vulnerability window. The OS loader executes `DllMain` for all statically linked dependencies *before* `Main()` runs — any `AppInit_DLLs` or DLL search-order hijacking payload executes unprotected. [ri:§3.3]

| Enforcement Method | Timing | Security Rating | Notes | Source |
|---|---|---|---|---|
| `SetProcessMitigationPolicy` (API) | Post-initialization | Low | TOCTOU vulnerable to early injection | [ri:§3.3] |
| `UpdateProcThreadAttribute` (Launcher) | Process creation | Medium | Requires trusted launcher binary | [ri:§5.2] |
| PE/Appx Manifest | Pre-initialization | High | No XML schema for ACG/CIG | [ri:§5.2] |
| **IFEO Registry Keys** | Pre-initialization | **Optimal** | OS loader enforces before first instruction | [ri:§5.2] |
| **WDAC (App Control)** | OS-Level | **Optimal** | Hypervisor-enforced, survives admin tampering | [ri:§6.3] |

**Recommended approach**: Configure IFEO `MitigationOptions` QWORD at `HKLM\...\Image File Execution Options\ThisIsMyPC.exe` during installation. WDAC policies serve as the ultimate fallback for enterprise/hardened environments — compiled to `.cip` files, loaded into EFI partition, enforced by HVCI. [ri:§5.2, §6.3]

**Bootstrapper timing constraint**: Unpackaged WinUI 3 apps use `Microsoft.WindowsAppRuntime.Bootstrap.dll` to dynamically load Windows App SDK framework DLLs via `LoadLibrary`. CIG must allow these Microsoft-signed DLLs to load before the bootstrapper initializes. IFEO enforcement handles this correctly since Microsoft-signed DLLs pass CIG validation natively. [ri:§3.2]

### 7.4 Data Storage Hardening

- `%APPDATA%\ThisIsMyPC` is user-writable by default — any standard, non-elevated process has full read/write/execute permissions [tm2:120]
- **Config poisoning**: malware modifies `settings.json` to alter update URLs, inject malicious command-line arguments, or flip boolean flags to silently initiate Owner Mode on next launch [tm2:122]
- **SQLite tampering**: forged `ChangeGroup` records could make the elevated ThisIsMyPC service write attacker-controlled values to `HKLM\...\Run` — a "confused deputy" attack requiring no zero-day [tm2:124]
- **Symlink attacks**: NTFS junctions redirect file I/O from `%APPDATA%` to protected system locations; demonstrated in CVE-2025-21204 where `MoUsoCoreWorker.exe` (SYSTEM) followed a junction from `ProgramData\...\Tasks` to overwrite arbitrary protected files [tm2:128-132]

**Mitigations:** [tm2:134]
- Programmatically set DACL on `%APPDATA%\ThisIsMyPC`: disable inheritance, restrict Write/Modify to Administrators + SYSTEM only
- Validate config file integrity on load (hash or signature check)

### 7.5 Log Injection Prevention (CWE-117)

- Serilog with unstructured text output is vulnerable to CRLF injection (`\r\n`); attacker injects fake log entries like `\r\n[INFO] User authenticated successfully` to forge audit trails [tm2:138-140]
- Disk exhaustion DoS: attacker bombards application with massive strings, exploiting the file sink [tm2:142]
- **Mandatory**: Use `CompactJsonFormatter` (CLEF) — JSON structure safely escapes injected CRLF within the value field, mathematically preventing structural line breaks [tm2:144]
- Enforce rolling file limits (10MB/file, 7-day retention) [tm2:144]

## 8. Context Menu Architecture

### 8.1 Win11 Bifurcated Menu System

- **Modern menu**: WinUI 3 XAML, out-of-process rendering via RPC to `dllhost.exe` surrogates, requires `IExplorerCommand` + cryptographic package identity (Sparse Manifest for unpackaged Win32 apps) [cm:16-24]
- **Legacy menu**: In-process GDI rendering via `IContextMenu` COM, accessible via "Show more options" or `Shift+F10` [cm:16]
- Full classic menu restored by creating `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` with `(Default)` set to empty string (not "value not set") [cm:32-38]
- **Latency cause**: Cross-process marshaling + GPU swap chain initialization for XAML/Acrylic + redundant invisible animation frames during cold start [cm:57-60]
- Single-item flyout rule: each application package gets exactly one top-level entry; multiple verbs are collapsed into a cascading submenu [cm:176]

### 8.2 Contribution Taxonomy

Four architectural categories populate context menus: [cm2:§1]

1. **Hardcoded/Canonical Verbs**: Cut/Copy/Paste/Rename/Share/Delete are hardcoded into `Windows.UI.FileExplorer.dll`; canonical verbs (`open`, `opennew`, `print`, `explore`, `properties`) are translated at runtime via MUI resource files and invoked programmatically via `ShellExecuteEx` with `lpVerb` [cm2:§1.1]
2. **Static Registry Verbs**: Registry-driven (`\shell\<verb>\command`), support `%1` (file path) and `%V` (directory path) arguments, conditional logic via `Extended`, `AppliesTo` (AQS), `HasLUAShield`; cascading via `SubCommands` or `ExtendedSubCommandsKey` pointing to `CommandStore\shell` [cm2:§1.2]
3. **Dynamic COM Handlers** (`IContextMenu`): In-process DLLs loaded into `explorer.exe`; receive `IDataObject` via `IShellExtInit::Initialize`; full `HMENU` access during `QueryContextMenu` with `idCmdFirst`/`idCmdLast` range; `IContextMenu2`/`IContextMenu3` for owner-drawn UI [cm2:§1.3]
4. **Modern IExplorerCommand**: Out-of-process via PackagedCom; declarative stateless methods (`GetTitle`, `GetIcon`, `GetState`, `GetFlags`); 1 top-level item per app identity, strict 1-level-deep submenus (via `ECF_HASSUBCOMMANDS` + `IEnumExplorerCommand`); deeper nesting silently discarded [cm2:§1.4, §7.2]

### 8.3 Explorer Filtering Pipeline

The Shell filters contributions in two phases: [cm2:§2]

1. **Pre-instantiation registry filtering**: Computationally inexpensive triage based on the class of the selected object; handlers not registered in the targeted taxonomy are categorically ignored before any COM loading occurs [cm2:§2.1]
2. **Post-instantiation programmatic filtering**: COM object is instantiated and fed selection context via `IShellExtInit::Initialize` (legacy) or `IObjectWithSelection` (modern); handler self-suppresses via `ECS_HIDDEN` (`IExplorerCommand::GetState`) or by returning 0 items during `QueryContextMenu` [cm2:§2.2]

**Background surface fallback**: When right-clicking `Directory\Background`, the `IShellItemArray`/`IDataObject` is empty. Handlers must implement `IObjectWithSite`, query `SID_STopLevelBrowser` → `IShellBrowser` → `IShellView::GetFolder` to retrieve the current directory path. Critical for "Open Terminal Here"-style commands. [cm2:§2.3]

**PowerRename case study**: Demonstrates post-instantiation keyboard-state filtering — inspects `CMF_EXTENDEDVERBS` flag during `QueryContextMenu` to conditionally suppress itself when user has not held SHIFT [cm2:§2.4]

### 8.4 Ghost Handlers

Three distinct vectors produce invisible-but-registered handlers: [cm2:§3]

1. **Benign (programmatic self-suppression)**: Globally registered handlers that self-suppress via `ECS_HIDDEN` when irrelevant to current context; still incurs DLL load + query performance penalty [cm2:§3.1]
2. **Malignant (orphaned registry pointers)**: Uninstallers fail to clean up `shellex` keys pointing to deleted DLLs (e.g., OneDrive `FileSyncEx` `{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}`); Explorer waits for I/O timeout per orphan — compounding "slow right-click" latency [cm2:§3.2]
3. **Architectural (Win11 segregation)**: Legacy `IContextMenu` handlers are instantiated and queried by the modern menu engine, then silently suppressed from the top-level view; they remain as ghosts, appearing only in "Show more options" [cm2:§3.3]

- **Implication**: ThisIsMyPC's orphan detection must scan for all three types — not just missing DLLs but also IContextMenu-only registrations that are invisible in the modern menu [cm2:§3]

### 8.5 Surface Inheritance: DesktopBackground vs. Directory\Background

- `DesktopBackground` inherits all registrations from `Directory\Background` (desktop is treated as a specialized folder view mapped to `CSIDL_DESKTOP`) [cm2:§4]
- Inheritance is strictly **unidirectional**: `Directory\Background` → `DesktopBackground` (union merge), but NOT reverse [cm2:§4]
- Desktop-exclusive commands (Display Settings, Personalize) must target `HKCR\DesktopBackground\shellex` specifically; they will not appear in folder backgrounds [cm2:§4]

### 8.6 Implementation Model for ThisIsMyPC

For managing context menu entries, the app needs to handle two handler types:

**Static Verbs** (`\shell\<verb>\command`): [cm:112-125]
- Entirely registry-driven, no code loading [cm:114]
- Support `MUIVerb` (localized name), `Icon`, `Position` (Top/Bottom), `Extended` (Shift-only) [cm:120-123]
- Safe disable: add `LegacyDisable` empty string value (non-destructive, instantly reversible) [cm:240]
- Alternative: `ProgrammaticAccessOnly` — hidden from GUI but still invocable by scripts [cm:242]
- 15-file selection limit (`MultipleInvokePromptMinimum` DWORD in `HKCU\...\Explorer`) [cm:125]

**Dynamic COM Extensions** (`\shellex\ContextMenuHandlers\{CLSID}`): [cm:127-133]
- Code loaded via `HKCR\CLSID\{GUID}\InProcServer32` DLL path [cm:131]
- Receive `IDataObject` with all selected files simultaneously — bypass the 15-file limit [cm:133]
- Safe disable: add CLSID to `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` (absolute master override, system-wide) [cm:250]
- Alternative: prepend `-` to the CLSID string value (dash prefix method, legacy) [cm:248]

### 8.7 The "New" Submenu Architecture

The "New" submenu operates on a separate framework from standard verbs/handlers, governed by the `ShellNew` registry key under `HKCR\.ext\ShellNew`: [cm2:§5]

| Value Type | Behavior |
|---|---|
| `NullFile` | Creates empty 0-byte file [cm2:§5] |
| `FileName` | Copies template from `%Windir%\ShellNew` (Office formats) [cm2:§5] |
| `Data` | Injects raw `REG_BINARY`/`REG_SZ` data into new file [cm2:§5] |
| `Command` | Executes custom command-line to generate file [cm2:§5] |

- **Windows 11 breaking change**: Requires `FriendlyTypeName` value in the ProgID's `auto_file` key (not in `ShellNew` itself); without it, the XAML engine suppresses the entry entirely [cm2:§5.1]
- **Implication**: ThisIsMyPC's "New" submenu management must validate both the `ShellNew` key and the `FriendlyTypeName` in the associated ProgID

### 8.8 Multi-Selection Logic

- Shell uses **strict set intersection** (not union) to determine visible verbs for mixed-type selections; only verbs universally applicable to every selected item are shown [cm2:§6.1]
- Mixed selections rapidly reduce to generic `HKCR\*` / `HKCR\AllFileSystemObjects` verbs (Cut, Copy, Delete, Properties) [cm2:§6.1]
- Static verbs spawn one process per file; 15-file threshold (`MultipleInvokePromptMinimum`) suppresses static verbs to prevent fork bombs [cm2:§6.2]
- Dynamic COM handlers receive all selected items as a single `IDataObject`/`IShellItemArray` array — bypass the 15-file limit entirely [cm2:§6.2]

### 8.9 Registry Scope Hierarchy

File resolution cascade (most specific to broadest): [cm:139-148]
1. `HKCR\.ext\shell` — specific extension (absolute highest priority) [cm:143]
2. `HKCR\<ProgID>\shell` — application association [cm:144]
3. `HKCR\SystemFileAssociations\<Kind>\shell` — perceived type (image, audio, etc.); persists even when default app changes [cm:145]
4. `HKCR\*\shell` — all standalone files (excludes directories) [cm:146]
5. `HKCR\AllFilesystemObjects\shell` — all files + directories [cm:147]

Directory resolution cascade: [cm:149-155]
1. `HKCR\Directory\shell` — physical folders only [cm:153]
2. `HKCR\Folder\shell` — physical + virtual folders (Control Panel, ZIP archives, etc.) [cm:154]
3. `HKCR\AllFilesystemObjects\shell` — universal [cm:155]

Additional scopes: [cm:84-96]
- `HKCR\Directory\Background\shell` — whitespace inside a folder (not clicking an item) [cm:84]
- `HKCR\DesktopBackground\shell` — desktop right-click (inherits from `Directory\Background`) [cm:92] [cm2:§4]
- `HKCR\Drive\shell` — root volumes (`C:\`, `D:\`) [cm:87]

### 8.10 Vendor Anomalies

- **7-Zip vs NanaZip**: 7-Zip refuses Sparse Manifests, relegated to legacy menu; community fork NanaZip wraps 7-Zip in AppX manifest with `IExplorerCommand` for top-level integration [cm:205-206]
- **PowerToys double-registration**: Dual registration in both `PackagedCom` and `shellex\ContextMenuHandlers` causes duplicate entries when classic menu hack is applied [cm:219-230]
- **MS Copilot**: "Ask Copilot" forcibly injected via system-level extension; removal requires adding `{CB3B0003-8088-4EDE-8769-8B354AB2FF8C}` to Blocked list [cm:217]
- **OneDrive FileSyncEx**: Orphaned `{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}` keys cause slow right-click when DLL is missing or AppLocker-blocked [cm2:§3.2]

## 9. Configuration Surface: Enforcement Mechanisms

The research identifies that registry writes alone are insufficient for many settings. Windows 11 uses multiple enforcement layers that actively resist user modifications. [cs:8-12]

### 9.1 Enforcement Mechanisms That Resist Modification

| Mechanism | What It Protects | How It Resists | Source |
|---|---|---|---|
| **ucpd.sys** (kernel filter) | Default app associations | Blocks registry writes even from SYSTEM; `NOT_STOPPABLE` | [kd:56-60] [cs:140-144] |
| **GPCache** (`updatepolicy.dll`) | Windows Update policies | Update Orchestrator reads cached values, ignores direct policy edits | [cs:43-46] |
| **Tamper Protection** | Defender registry hives | Silently reverts unauthorized modifications on reboot/service cycle | [cs:66-68] |
| **TrustedInstaller ownership** | Network/metered connection keys (`DefaultMediaCost`) | Standard admins lack write permission; requires programmatic ownership transfer + ACL manipulation | [cs:106-108] |
| **Scheduled Tasks** | UCPD velocity, Edge shortcuts, telemetry | Re-enable disabled services/drivers on reboot or idle | [kd:60] [cs:144] |
| **Web Experience Pack** | Bing search, Copilot, Start Menu | Feature updates and background pack installs overwrite user registry settings | [cs:20-22] [cs:244] |

### 9.2 Configuration Categories and Key Settings

The control surface spans 12 subsystems with ~50 documented registry pathways:

| Category | High-Impact Settings | Enforcement Complexity | Source |
|---|---|---|---|
| **Privacy/Telemetry** | Bing search (`BingSearchEnabled`), Copilot (`TurnOffWindowsCopilot`), DiagTrack, Advertising ID | Medium — reverted by feature updates | [cs:16-40] |
| **Windows Update** | Version pinning (`TargetReleaseVersion`), driver exclusion (`ExcludeWUDriversInQualityUpdate`), auto-reboot suppression | High — GPCache sync required | [cs:41-63] |
| **Security/Defender** | Real-time protection, exclusions (policy path bypass), SmartScreen | Very High — Tamper Protection + wdfilter.sys | [cs:64-82] |
| **Default Apps** | Browser, PDF viewer associations (UserChoice hash) | Very High — ucpd.sys kernel driver | [cs:138-152] |
| **Services** | Per-user templates (`CDPUserSvc`), SysMain, DiagTrack, Print Spooler | Low — standard `Start` DWORD | [cs:83-103] |
| **Power Management** | Power plan GUIDs, Modern Standby (`PlatformAoAcOverride`), hibernate | Medium — hidden plans, deprecated `CsEnabled` toggle | [cs:123-137] |
| **Appearance** | Transparency (`EnableTransparency`), ClearType, DPI scaling, taskbar alignment | Low — HKCU, immediate effect | [cs:153-171] |
| **Network** | DoH policy, metered connection (TrustedInstaller-owned), per-machine proxy | High — TrustedInstaller ownership | [cs:104-122] |
| **Context Menu** | Classic menu restore, orphan cleanup, bloatware removal | Medium — dual handler system | [cm:32-38, 240-250] |
| **Gaming** | Game DVR (`AppCaptureEnabled`), HAGS (`HwSchMode`), Auto Game Mode | Low — standard registry | [cs:183-195] |
| **Accessibility** | Sticky Keys (`Flags` bitmask), Filter Keys shortcut | Low — HKCU flags | [cs:172-182] |
| **Accounts** | SCOOBE suppression (`ScoobeSystemSettingEnabled`), passwordless disable, welcome screen | Low — standard registry | [cs:220-234] |

### 9.3 User Sentiment Priority (Ranked by Friction Intensity)

From exhaustive analysis of r/Windows11, r/sysadmin, Microsoft Answers, and specialist forums: [cs:235-237]

1. **Ecosystem Encroachment** — Edge shortcuts recreating after every update, Bing web searches in Start Menu leaking queries to cloud endpoints (pervasive, fragile fixes constantly reverted by updates) [cs:239-245]
2. **Autonomy Subversion** — UCPD blocking programmatic default app changes; cryptographic hash rejects external modifications; "cat-and-mouse" with Microsoft patching the bypass surface [cs:247-253]
3. **Workflow Interruption** — SCOOBE full-screen nags with no permanent opt-out (only "Remind me in 3 days"), post-update settings resets (dark pattern) [cs:255-261]
4. **UI Performance** — File Explorer sluggishness despite NVMe+modern CPU; F11 double-tap rendering bug as workaround; context menu XAML latency [cs:263-269]
5. **MS Account Enforcement** — Forced internet + Microsoft Account during OOBE; `Shift+F10` → `OOBE\BYPASSNRO` as undocumented bypass [cs:271-277]
6. **Invasive AI** — Copilot injected into taskbar/Edge/Office without consent; DiagTrack consuming resources with zero user benefit [cs:279-285]

## 10. Architectural Blueprint Summary

### Tier 1: Mandatory (Pre-Release Blockers) [tm2:190-196]

- IPC: `FILE_FLAG_FIRST_PIPE_INSTANCE` + authenticated RPC with `PKT_PRIVACY` [tm1:84-88]
- Kernel: `IoCreateDeviceSecure` + `FILE_DEVICE_SECURE_OPEN` + SDDL `D:P(A;;GA;;;SY)(A;;GA;;;BA)` [kd:260-262] [tm1:104]
- NativeAOT: `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` on all P/Invoke [tm2:100, 194]
- Updates: Authenticode + out-of-band GPG verification + reject unsigned Velopack packages [tm2:52-54, 192]
- Storage: DACL enforcement on `%APPDATA%\ThisIsMyPC` (Admins + SYSTEM only) [tm2:134, 195]
- Logging: Serilog `CompactJsonFormatter` (CLEF) with rolling limits [tm2:144, 196]
- Enforcement: Synchronize GPCache when modifying update policies [cs:295-296]

### Tier 2: Pre-Stable Release [tm2:198-202]

- Bytecode: Cryptographic signing + in-kernel static verifier + `__try`/`__except` [tm2:200]
- UI: Mandatory 5-second delay timer + randomized visual CAPTCHA for Owner Mode transition [tm2:201]
- Builds: Reproducible NativeAOT compilation in containerized CI/CD [tm2:202]
- Context Menu: Orphan detection (3 ghost handler types) + dual-handler (static verb + COM extension) management + "New" submenu validation [cm:256-258] [cm2:§3, §5]
- User-mode hardening: `<ControlFlowGuard>Guard</ControlFlowGuard>` in `.csproj`; IFEO-based ACG (`ProcessDynamicCodePolicy`) + CIG (`ProcessSignaturePolicy`) enforcement on both GUI and service processes; WDAC Supplemental Policy for any unsigned DLL dependencies [ri:§2.1, §5.2, §4.3]

### Tier 3: Defense-in-Depth [tm2:204-206]

- Callback target pinning: hardcode protected key list in driver binary, refuse dynamic lists from user-space [tm2:206]
- Minifilter: NOT_STOPPABLE + full reparse point resolution [tm1:160, 168]
- Process protection: `ObRegisterCallbacks` to strip sensitive handle rights from unauthorized callers [kd:212-214]

### Comparative Positioning [tm2:210-218]

| Feature | Riot Vanguard | CrowdStrike | ThisIsMyPC |
|---|---|---|---|
| Load Time | Boot-start (ELAM) [kd:128-132] | Boot-start minifilter [kd:140] | Dynamic load (explicit opt-in) |
| Data Parsing | Proprietary encrypted [tm2:216] | Unsigned dynamic templates (caused BSOD) [tm2:216] | Cryptographically verified bytecode [tm2:216] |
| Persistence | KiCpuTracingFlags, ObRegisterCallbacks [tm2:215] | File system + registry minifilters [tm2:215] | Static CmRegisterCallbackEx (app hives only) [tm2:215] |
| Auditability | Closed-source, obfuscated [tm2:217] | Closed-source, proprietary [tm2:217] | Open-source (GPLv2), reproducible builds [tm2:217] |

**On auditability**: Open source provides the *property* of auditability — it does not constitute a *guarantee* that the code has been audited. Until a formal security audit or bug bounty program is established, the transparency claim is aspirational. The project should publish a `security.txt` (RFC 9116), a coordinated disclosure policy, and a CVE request workflow before v1.0 to make the auditability claim actionable.

### Boot-Start Exclusion and Reactive Enforcement

ThisIsMyPC cannot achieve boot-start loading. ELAM certification requires HLK test passage and explicit Microsoft approval for the ELAM-specific EKU [kd:72-76] — Attestation signing (which PawnIO uses) does not qualify [kd:37]. Microsoft will not grant ELAM certification to a tool designed to override their own enforcement mechanisms. Any alternative boot-start technique would require rootkit-level methods (disabling Secure Boot, unsigned boot drivers, kernel patching) — exactly the kind of opaque, unauditable behavior the project exists to oppose.

The enforcement model is therefore **reactive**: a demand-start service (`start= demand`) that detects and re-applies user-chosen configuration after boot. For the settings ThisIsMyPC targets (telemetry, default apps, update policy, UI preferences), the consuming components don't read these values until well after the user reaches the desktop, so the brief window between boot and service start is invisible in practice.
