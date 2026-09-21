# Read-only LibreHardwareMonitor sensor port

Source repository: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

Pinned revision: `dc51e75bd97b15ce17ded0885e67bad47be0765b` (0.9.6 source tree).
The retained source uses MPL-2.0. Keep `LICENSE` and the upstream
`THIRD-PARTY-NOTICES.txt` with this source. The notices describe the complete upstream
tree; this port does not distribute its drivers or third-party libraries.

## Retained sources

- `NvApi.cs` derives from `LibreHardwareMonitorLib/Interop/NvApi.cs`.
- `NvmlReadOnly.cs` retains initialization, PCI lookup, power reads and shutdown
  from `LibreHardwareMonitorLib/Interop/NvidiaML.cs`.
- `src/ThisIsMyPC.Interop.Sensors/NvidiaSensorReader.cs` derives its read-only
  queries, units and thermal field mappings from
  `LibreHardwareMonitorLib/Hardware/Gpu/NvidiaGpu.cs`.

This is a narrow source port, not the complete LibreHardwareMonitor package.
The project compiles the retained sources directly. There are no new NuGet
dependencies, driver resources, generated machine code, WMI calls, control
setters, fan-curve writes, or I2C operations.

## Local changes

- Remove fan-control setters and I2C entry points from NvAPI.
- Require a verified, already mapped System32 NvAPI image. The app's existing
  vendor preloader maps it before CIG. The backend checks the same publisher.
- Load NVML only from System32 after signature verification. Accept NVIDIA or
  Microsoft's Windows Hardware Compatibility Publisher. If CIG rejects the
  image, other readings remain available and the snapshot reports missing power.
- Query NVML by unique PCI bus/slot only, with no cross-API ordinal fallback.
  NVAPI lacks PCI domain information, so only domain zero is attempted.
- Balance each successful NVML initialization with shutdown. Keep the native
  library mapped, as other app modules can use the same image.
- Poll on a serialized worker task. Cancellation stops a caller's wait without
  overlapping native reads. Disposal joins the outstanding task off the UI thread.
- Keep snapshot history in Core, with bounded samples and sensor identifiers.
  GPU identifiers include bus, slot and adapter ordinal to prevent duplicate IDs.
  They are session identifiers; they are not persistence keys across GPU changes.

The Windows memory and basic battery readers are native app code. Memory units
are binary GiB/MiB despite the contract's `Gigabytes`/`Megabytes` enum names.
Battery coverage is Windows charge percentage and estimated remaining hours.
CPU, motherboard, storage, SPD, PawnIO, AMD and Intel sensors remain unavailable.

## Verification

Run Core's `HardwareSensorHistoryTests` for gaps, stale readings, window limits,
duplicate snapshots and finite statistics. A NativeAOT diagnostic host must enable
strict ACG and CIG, preload NvAPI through the production vendor loader, then sample
this backend. Debug or managed-only success does not prove release compatibility.
Hardware reads belong to diagnostic checks, not CI tests.
