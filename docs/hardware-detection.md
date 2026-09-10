# Shared hardware detection

Hardware modules consume `IHardwareDetectionService` from `ThisIsMyPC.Core.Hardware`.
The App registers the native `HardwareDetectionService` as a singleton. Home uses the same service.
`GetSnapshotAsync` shares a cached scan. `RefreshAsync` reuses an active scan or starts a new scan when none is running.
Cancellation stops that caller waiting. Native reads run on a worker thread and are not forcibly interrupted.

`HardwareSnapshot` exposes:

- `Facts`: existing `ObservedHardwareFacts` for `HardwareCompatibilityPolicy.Decide`.
- `Firmware`: system manufacturer/model, separate motherboard identity, BIOS, chassis codes, and memory devices.
- `Chipset`: name and evidence source. A motherboard-model inference is labeled explicitly.
- `Devices`: present Windows Plug and Play devices, class names, and hardware IDs.
- `Issues`: incomplete native probes. Missing information never grants hardware control.

Hardware IDs are evidence only. Do not execute a path, load a driver, or grant device access from this inventory.
The collector does not read serial numbers or UUIDs, write device settings, load drivers, or make network requests.

## Evidence limits

SMBIOS identifies system, motherboard, enclosure, BIOS, and memory separately.
Firmware placeholders become missing values. Windows BIOS registry values provide identity fallbacks.
Only motherboard Type 2 records supply board identity; daughterboards do not.
Memory speed means configured transfer rate in MT/s, not the module's advertised maximum.

Present-device enumeration includes inactive display adapters, unlike the earlier desktop-attached GPU list.
It excludes removed device records. Storage names can include USB drives and virtual disks.

An explicit chipset name in a PCI System device description takes priority.
Otherwise, recognized chipset tokens in the motherboard model provide a labeled inference.
Generic bridge IDs and CPU families do not identify a chipset. Unknown models remain unidentified.
Chipset labels are presentation evidence and must not select a control protocol or driver.

Chassis types feed the existing conservative form-factor classifier. Battery and power role only corroborate them.
The ATKACPI probe checks the DOS device name without opening the driver or issuing an IOCTL.
Presence does not establish support for a particular ASUS model.

Registered companion names and running process names provide positive observations only.
No hit remains unknown because a portable application can live anywhere.
Process names do not prove trust, executable location, or device ownership. Never launch from a process-name observation.
OpenRGB SDK readiness, device ownership, internal display evidence, and sensor backend readiness belong to their module adapters.
Those facts remain unobserved here. Refresh after installing or closing a companion.

## Sources and verification

- [DMTF SMBIOS 3.8.0](https://www.dmtf.org/sites/default/files/standards/documents/DSP0134_3.8.0.pdf), types 0, 1, 2, 3, and 17.
- [Windows firmware tables](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsystemfirmwaretable).
- [Present device enumeration](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw).
- [ASUS B550-F specifications](https://rog.asus.com/motherboards/rog-strix/rog-strix-b550-f-gaming-wi-fi-model/spec/), used to check this host's board-derived chipset label.

Pure tests cover malformed firmware, string boundaries, memory units, chassis lock bits, and chipset ambiguity.
The read-only `HardwareDetectionDiagnostics` test writes its host report under `artifacts/diagnostics/hardware-live/`.
