# Hardware integration boundaries

Investigated on 2026-09-21 for Cooling activation and Monitoring. These are implementation constraints, not completed features.

## Cooling

The installed FanControl executable reports version 226. No FanControl process was running during this inspection.
The current editor intentionally supports version 226 configuration files only.

The documented `FanControl.exe -c yourConfig.json` command requests configuration loading in a new or existing process.
It does not document an active-profile or active-curve readback operation.
The plugin interface exposes injected sensors and controls, not the existing configuration model.
The installed plugin documentation exposes `IPluginApplicationControl.ShowMainWindow`, but no configuration readback.

Earlier live testing established that a CLI switch changed a visible card nickname.
It did not establish active curve behavior. `CACHE.CurrentConfigFileName` remained stale during that experiment.
Neither that cache value nor process exit zero can serve as activation acknowledgement.

Reliable automatic activation still needs an independent observation of runtime state.
UI Automation is a candidate for a version-specific diagnostic, not yet a supported production contract.
An elevated FanControl instance also requires handling the caller privilege boundary.
Do not broaden the privileged broker into an arbitrary executable launcher.

Keep file-save undo separate from runtime restoration until the previous active configuration can be captured reliably.
See [configuration integration](../fancontrol-configuration.md) for the existing editor and experiment evidence.

Sources: [command documentation](https://getfancontrol.com/docs/),
[plugin API](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins),
[maintainer's CLI recommendation](https://github.com/Rem0o/FanControl.Releases/discussions/3975).

## Monitoring

Reviewed LibreHardwareMonitor source at commit `dc51e75bd97b15ce17ded0885e67bad47be0765b`.
The existing Hardware tab is a placeholder. `Core.Monitoring` tracks startup changes and is unrelated to sensor sampling.

Unmodified LibreHardwareMonitor is not compatible with the current release restrictions:

- `Computer.Open` calls `OpCode.Open` unconditionally.
- `OpCode.Open` allocates executable writable memory for CPUID/RDTSC routines. Strict ACG prohibits that allocation.
- The project declares AOT compatibility while suppressing several trimming and dynamic-code warnings. This requires runtime verification.
- IPMI uses WMI. That path is outside the project's permitted interop design.
- Sensor constructors also need a write audit. Selecting a monitoring backend does not establish that all initialization is read-only.

The isolated compatibility probe evaluates a pinned source patch, without adding a production dependency.
CPUID can use `X86Base.CpuId`. RDTSC-dependent readings need a static implementation or explicit omission.
Do not substitute fabricated values or relax ACG/CIG to make the library load.

The patched NativeAOT probe passed three live samples with ACG and CIG enabled before and after sampling.
It read memory and RTX 4080 sensors without driver installation or fan-control writes.
CPU, motherboard, storage, actual battery hardware, and AMD/Intel GPUs remain unverified.
The broad upstream project still produces warnings from unused dependencies. Production integration requires a narrower dependency closure.

Initial sampling should use memory and supported GPU readings, then expand after guarded runtime checks.
Missing PawnIO or inaccessible motherboard sensors must not hide unrelated working sensors.
CPU/motherboard access must use fixed, bounded backend operations within the existing privilege architecture.
Do not accept arbitrary register addresses, driver modules, or native paths from UI requests.

Sources: [project](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/dc51e75bd97b15ce17ded0885e67bad47be0765b/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj),
[Computer](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/dc51e75bd97b15ce17ded0885e67bad47be0765b/LibreHardwareMonitorLib/Hardware/Computer.cs),
[OpCode](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/dc51e75bd97b15ce17ded0885e67bad47be0765b/LibreHardwareMonitorLib/Hardware/OpCode.cs).

## Installed release baseline

Read-only inspection found installed version `0.1.2-test-04`.
App, Broker, and Service carry valid No More Secrets, LLC Authenticode signatures.
All installed first-party binaries pass the repository PE hardening checks.
The Owner Mode service was stopped; the app was not running.

This verifies disk signatures and static PE flags only. It does not verify runtime ACG/CIG, hardware operations, or undo.
Live installation remains owned by Claude Code. Coordinate the final installed build before functional release checks.
