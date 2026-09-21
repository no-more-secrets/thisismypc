# Installed release validation, 2026-09-21

This pass inspected the existing installation without changing settings, starting
services, applying hardware controls, or running the installer. Claude Code owns
the concurrent installer work. Do not replace that installation during this pass.

## Installed identity

Location: `%ProgramFiles%\NMS\ThisIsMyPC\current`.

The package metadata in `sq.version` identifies `ThisIsMyPC`, version
`0.1.2-test-04`, channel `win`, RID `win-x64`. All three first-party executables
report product version `0.1.2-test-04` and file version `0.1.2.0`.

| Executable | SHA-256 |
|---|---|
| ThisIsMyPC.App.exe | `F2948473030109F118F171B7C9E3B937CB59596EFFC6C6500FC8BD67A574546F` |
| ThisIsMyPC.Broker.exe | `6E3E10A17BA881B149185903616AA878F96E82C81E3176E1BD10B489F7F35788` |
| ThisIsMyPC.Service.exe | `2536F46F73214691D41621DCB8F7A4862F431A18D23E1993B4675B6BC9053B9A` |

Authenticode reported Valid for those executables, signed by No More Secrets, LLC.
It also reported Valid for the four installed native DLLs: `av_libglesv2.dll`,
`e_sqlite3.dll`, `libHarfBuzzSharp.dll`, and `libSkiaSharp.dll`.

This identity does not establish which uncommitted changes the installed package
contains. In particular, the new Monitoring backend proof below uses a separate
diagnostic executable built from the working source.

## Evidence collected

| Check | Evidence | Result and limit |
|---|---|---|
| Installed PE headers | `artifacts/diagnostics/installed-release-hardening.log` | Seven installed PEs pass x64, ASLR, high-entropy VA, DEP, CFG, /GS, CET metadata, unwinding and W^X checks. These are static properties. |
| Actual running app identity | Process image path matches the installed `ThisIsMyPC.App.exe` | Confirms an installed app process was running. Does not prove every page or operation works. |
| Actual app process mitigations | `artifacts/diagnostics/installed-release-runtime-mitigations.json`, read with `Get-ProcessMitigation` | ACG ON, thread opt-out OFF, Microsoft-only CIG ON, CFG ON and DEP ON. This is runtime evidence, not a PE inference. |
| Owner Mode service configuration | Read-only Service Control Manager queries | Service `ThisIsMyPC` points to the installed service executable, uses LocalSystem, and has Automatic startup. It was stopped. No service start was attempted. |
| Narrow Monitoring backend | `artifacts/diagnostics/monitoring-production-probe/publish.log` and `run.log` | Separate NativeAOT host publishes without IL2xxx/IL3xxx warnings and reads memory plus NVIDIA sensors under ACG and CIG. It is not the installed app. |
| Monitoring publication notices | Diagnostic publish output | License, attribution README and upstream notices propagate to `ThirdPartyNotices/LibreHardwareMonitor`. |

The Monitoring host read temperatures, clocks, GPU/VRAM load, fan RPM, voltage,
memory and wattage across three samples. ACG and CIG remained enabled before and
after sampling. No battery was present, so battery readings remain unverified.
This does not close CPU, motherboard, storage, AMD, Intel or driver-backed coverage.

## Existing validation facilities

| Facility | What it can establish | What it cannot establish |
|---|---|---|
| `Get-AuthenticodeSignature`, `Get-FileHash`, package metadata | Installed artifact identity and signature validity without launching it | Runtime restrictions, correct device behavior, or correspondence to a specific source revision |
| `tools/check-binary-hardening.ps1` | Static PE protection flags and section properties | Active ACG/CIG or functional operations |
| `Get-ProcessMitigation` on the existing installed PID | Active process mitigation policy without injecting code or changing the process | Correct Broker authorization, Owner Mode enforcement or module operation |
| Service Control Manager query | Service path, account, startup type and state | A working service handshake, baseline, drift correction or pause behavior |
| `tools/AcgLauncher` | Starts a chosen executable with strict ACG and checks startup/window rendering | It is not a passive attachment tool. It terminates its child during cleanup and does not replace installed module acceptance. |
| `MainWindowWalkthroughTests` | Real module scans and page rendering in the source test host, with safe service swaps | The installed signed executable or its actual release trust boundaries |
| `HardwareDetectionDiagnosticTests` | Live hardware facts and compatibility decisions in a test host | Release UI operation or successful writes |
| `NativeLightingLiveTests.Detect_ListsThisPcsDevices` | Live lighting enumeration and readable device state | Installed NativeAOT operation, saved lighting, or write/undo correctness |
| `NvApiUnderCigTests` | Preloaded NvAPI remains usable after CIG in its isolated test host | Full installed app ACG/CIG behavior |
| `DdcTimingDiagnosticTests.TimeColdAndWarmScans` | Monitor enumeration and readable DDC capabilities | Brightness/contrast writes and restore behavior |
| `OwnerModeServiceTests` | App orchestration against fake service/install dependencies | A live SYSTEM service or real drift enforcement |

Diagnostic hardware fixtures must use their exact read-only test filters.
Do not run every diagnostic fixture as a substitute. Some tests can mutate live
state, including lighting when `TIPC_LIGHTING_WRITE=1` is present.

The installed app currently has no documented command-line module walkthrough or
read-only acceptance endpoint. Source tests cannot silently become installed tests.
Repeated passive mitigation and identity queries are safe while the app runs.
Installed module reads still require navigation in that app and recorded results.

## Remaining acceptance matrix

| Area | Current evidence | Required next evidence |
|---|---|---|
| Installed startup | Existing signed process runs with ACG/CIG | Fresh start from Start, no UI elevation prompt, responsive Home and Settings |
| Module reads | Source walkthrough and individual diagnostic facilities exist | Open every module in the new installed build; verify scans finish and errors are actionable |
| Cooling | Source profile workflow is being completed | Save, activate and confirm the selected curves in running FanControl; restore the original profile |
| Monitoring | Narrow guarded backend proof passed | Installed Monitoring view updates, survives navigation and closes without polling leaks; verify supported readings against a trusted reference |
| Lighting | Read-only detection and CIG test facilities exist | Change a reversible color/mode in the installed app, verify the device, restore it; test persistent storage separately |
| Display | Read-only DDC diagnostic exists | Change brightness/contrast, verify the monitor, and restore original values |
| System settings and undo | Source tests cover policy and pending-change paths | Apply one reversible user setting and one machine setting through the installed Broker; verify exact before-state restoration |
| Owner Mode | Installed service exists but is stopped | Enable with consent, verify authenticated status and binding, create a controlled drift, observe correction, test pause/resume and restore the original state |
| Release security boundary | Actual UI ACG/CIG verified; all installed PE signatures valid | Exercise authorized Broker requests, denied callers and service behavior in a disposable acceptance environment |
| Installer and updater | Existing installation has a signed identity | Complete fresh installation, upgrade, reinstall, older-version refusal, removal and in-app update using the finished installer |

Do not mark signature checks or a diagnostic-host pass as evidence for a live
apply, undo, drift correction, or installed hardware operation.

## Prerequisites and user checks

Finish the installer before replacing the current installation. Build a fresh
signed candidate containing the reviewed changes and record its identity again.
Use the [installer acceptance checklist](../release/installer-acceptance.md) on a
disposable Windows machine for installation and security-boundary tests.
Use actual compatible hardware for device operations and record original settings.

These are the remaining user-facing checks after that candidate is installed:

1. Open ThisIsMyPC from Start.
2. Open each module and wait for its contents.
3. Open Monitoring and check that readings change.
4. Leave Monitoring and return to check that readings resume.
5. Open Cooling and activate a saved profile.
6. Open FanControl and confirm that the selected curves match.
7. Restore the original cooling profile.
8. Change one reversible setting and apply it.
9. Open History and undo that change.
10. Confirm that the original setting returned.

Owner Mode and hardware-write checks need an agreed disposable test state first.
The service was not started, and no policy or hardware value changed in this audit.
