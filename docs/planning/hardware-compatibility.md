# Hardware compatibility: facts and decisions

Batch 1 of the shared hardware detection in [v1-completion-plan.md](v1-completion-plan.md) section 5. Landed 2026-09-06 as a pure Core layer with a fake-fact test matrix. Nothing here probes hardware, launches programs or writes anything. Detection and App wiring are the next batches; their exact shape is listed below.

## What exists (Core, `src/ThisIsMyPC.Core/Hardware/`)

| Type | Role |
| --- | --- |
| `ObservedHardwareFacts` | Input record: identity, form-factor evidence, companion observations, ATKACPI presence, OpenRGB server state, sensor backend state. Every optional field means "not observed" when null. |
| `MachineIdentity` | Manufacturer and model normalized from the registry strings the Home tab already shows. Firmware placeholders ("To Be Filled By O.E.M.", "System manufacturer", "Default string", ...) collapse to null. Vendor is Asus, Other, or Unknown. |
| `FormFactorEvidence`, `FormFactorClassifier` | Desktop, Laptop, or Unknown from SMBIOS chassis type first, power platform role second. Battery and internal panel only corroborate. |
| `CompanionObservation` | Per companion: installed, running, and the domains it was observed to own. Three separate facts. A companion with no observation at all was never checked; that is Unknown, not absent. `NotInstalled(app)` records a confirmed absence. |
| `GHelperSupportCatalog` | Verified, Unverified, Unsupported, Unknown for G-Helper. Driver gate first, then the model list. `VerifiedModels` is empty on purpose. A running G-Helper process is not support evidence. |
| `HardwareCompatibilityPolicy` | `Decide(facts, options)` returns one `HardwareTabDecision` per `HardwareDomain` plus the identity conclusions. |
| `HardwareTabDecision` | Availability, backend, one or two sentences of explanation, evidence lines, conflict notes, an Install or Open action, `Operations`, `ControlsVisible`, `LiveWritesAllowed`. `ToModuleAvailability()` bridges to the existing `ModuleAvailability` record. |
| `HardwareOperations` | Flags: InstallCompanion, OpenCompanion, ReadSensors, WriteDevices. Availability says a backend applies; Operations says what the tab may do. Available never implies WriteDevices. |

Not duplicated: SKU detection stays in `CapabilityDetector`. The ATKACPI and OpenRGB probes there feed `AsusPlatformDriverPresent` and the OpenRGB installed flag; they are not re-implemented.

## Decision rules (the whole policy)

Availability values: Available, Unavailable, Unknown, PendingVerification, Conflict. Unknown is never rounded up.

**Operations, separate from availability.** The action grants InstallCompanion or OpenCompanion. Only an Available tab adds its domain operation, and only Lighting has one that writes: Lighting adds WriteDevices, Monitoring adds ReadSensors, System Control and Cooling add nothing because G-Helper and FanControl do the hardware work themselves. `LiveWritesAllowed` is true only with WriteDevices, so an installed FanControl or a ready sensor backend never authorizes a write.

**Unobserved companions.** A companion with no `CompanionObservation` was never checked. The tab reads Unknown with no action. Only a confirmed `NotInstalled` observation produces an Install offer, so `ObservedHardwareFacts.Empty` offers nothing on any tab.

**Form factor.** Only a recognized SMBIOS chassis class decides (portable: 8, 9, 10, 14, 30, 31, 32; stationary: 3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24, 34, 35, 36; DMTF SMBIOS "System Enclosure or Chassis Types"). If both classes appear, the answer is Unknown and nothing breaks the tie. The power platform role from PowerDeterminePlatformRoleEx is corroborating only: Microsoft documents that it reads the ACPI FADT preferred PM profile and, when the FADT provides none, infers Mobile from battery presence (https://learn.microsoft.com/en-us/windows/win32/api/powerbase/nf-powerbase-powerdetermineplatformroleex). An API-only role can therefore be the same battery guess the battery-alone rule rejects, so the classifier records it as a reason (agreeing or disagreeing with the chassis) and never lets it decide. A battery or an internal panel on their own leave the answer Unknown and say so in the reasons. Consequence for detection: the RSMB firmware table parse is the load-bearing source; without it every machine reads Unknown.

**System Control (G-Helper).** Driver gate first, then the model list. A running G-Helper process is only an evidence line; it starts on unsupported machines too.

| Facts | Result |
| --- | --- |
| Manufacturer placeholder or missing | Unknown: "manufacturer could not be read" |
| Form factor Unknown | Unknown |
| Desktop | Unavailable: System Control is for laptops |
| Non-ASUS laptop | Unavailable, names the vendor; no alternative offered yet |
| ASUS laptop, ATKACPI absent (G-Helper running or not) | Unavailable: driver G-Helper relies on is missing |
| ASUS laptop, ATKACPI not probed | Unknown; Open if G-Helper is confirmed installed |
| ASUS laptop, ATKACPI present, model not in `VerifiedModels` (G-Helper running or not) | PendingVerification; Open only if G-Helper is installed, never Install |
| ASUS laptop, ATKACPI present, model in `VerifiedModels` | Available; Open if installed, Install if confirmed absent, nothing if not checked |

**Lighting (OpenRGB).** Not checked: Unknown. Confirmed not installed: Unavailable with Install. Installed, not running: Unavailable with Open. Running, SDK server not probed: Unknown. Running, server not answering: Unavailable with Open. Server answering, device list not read: Unknown, no writes. Server answering, device count 0: Unavailable. Server answering with a negative (invalid) count: Unknown, no writes, no action. Server answering with a count above zero: Available with Open and WriteDevices. Laptops are not excluded; the evidence says lighting depends on what OpenRGB finds.

**Cooling (FanControl).** Not checked: Unknown. Installed (running or not): Available with Open only; the copy says "open it to manage fan curves" and never claims FanControl owns anything. A running process is not ownership; a loaded configuration is a separate observation and appears only as an evidence line. Confirmed not installed: Unavailable with Install. On an ASUS laptop the evidence notes that fan modes also live in G-Helper. ThisIsMyPC opens FanControl; it never drives its configuration (backlog: opaque JSON, banned shell-out).

**Monitoring (LibreHardwareMonitor).** Follows `SensorBackendState`: NotIntegrated gives PendingVerification (the state this build is in), DriverMissing gives Unavailable, Ready gives Available with ReadSensors only. HWiNFO or LibreHardwareMonitor running as apps are never conflicts.

**Conflicts, applied uniformly after the per-tab rule.** A running companion other than the tab's own backend whose observed ownership includes the domain turns the tab into Conflict: no action, no operations, explanation names the program. A running "likely interferer" without observed ownership adds an advisory conflict note and changes nothing else. Installed-but-not-running programs only add an evidence line; unobserved programs add nothing. Likely interferers per domain: System Control: Armoury Crate. Lighting: SignalRGB, Armoury Crate, G-Helper. Cooling: G-Helper, Armoury Crate. Monitoring: none.

**Debug visibility override.** `HardwareCompatibilityOptions.ShowAllControls` sets `ControlsVisible` true on every tab and nothing else. A theory over ten scenarios (conflict, available, unknown device count, invalid device count, pending laptop, installed FanControl, ready sensors, role-only laptop guess, unobserved, empty) asserts availability, operations, conflicts, evidence, actions, explanation and backend are identical with the override on and off.

## Test matrix

`tests/ThisIsMyPC.Core.Tests/Hardware/`: 125 tests, all fake facts, no live reads. The builder gives every machine a confirmed NotInstalled for each companion it does not name (detection ran); `Unobserved(facts)` models detection never running.

```
dotnet test tests/ThisIsMyPC.Core.Tests --filter "FullyQualifiedName~ThisIsMyPC.Core.Tests.Hardware"
```

## Detection still needed (batch 2, Interop and App)

Each fact, the source, and whether that source already exists in this repo.

| Fact | Source | Status |
| --- | --- | --- |
| Manufacturer, model | `HKLM\HARDWARE\DESCRIPTION\System\BIOS` SystemManufacturer, SystemProductName | Present: `SystemIdentityService` reads both. Feed them to `MachineIdentity.From`. |
| SMBIOS chassis types | `GetSystemFirmwareTable('RSMB')` (kernel32), parse Type 3 structures, byte at offset 5, low 7 bits | New LibraryImport in `Interop.Win32`. No WMI (banned). |
| Platform role | `PowerDeterminePlatformRoleEx(POWER_PLATFORM_ROLE_V2)` in powrprof.dll | New import next to the existing powrprof imports in `Interop.Win32/Power/NativePower.cs`. Corroborating only; the API falls back to a battery guess without an FADT profile. |
| System battery | `GetSystemPowerStatus` BatteryFlag not 128 or 255 | Present: `DdcMonitorService` line 131 uses it for the laptop panel row. Expose it once instead of duplicating. |
| Internal panel | Display module laptop-panel path | Present in `Modules.Display`; expose the boolean. |
| ATKACPI driver | `System32\drivers\atkwmiacpi64.sys` | Present: `CapabilityDetector` (`SystemCapability.AsusAtkacpi`). |
| OpenRGB installed | `%AppData%\OpenRGB` directory | Present: `CapabilityDetector` (`SystemCapability.OpenRgb`). Note: that folder appears after first run, so "installed" here means "has been run". Add an uninstall-key or winget check before trusting it for Install offers. |
| OpenRGB running, SDK reachable, device count | Process name plus TCP connect to the SDK server, then the SDK controller-count request | New. Default SDK port 6742 and the protocol come from the OpenRGB wiki (unverified until read against the pinned version). |
| FanControl installed | Sam's machine: a scheduled task named FanControl whose Start In folder holds the exe (backlog, Startup module) | Task-based detection is verified on one machine. Install folder, autorun entry and winget id `Rem0o.FanControl` are unverified. |
| FanControl running | Process name | New; exe name unverified. |
| G-Helper installed | Portable exe; candidates: `%AppData%\GHelper\config.json`, an HKCU Run entry | Unverified. No winget id confirmed; the Install action may need a GitHub-release download path through the Software module. |
| G-Helper running | Process name `GHelper` | Unverified. |
| Armoury Crate installed, running | Uninstall key; services such as ArmouryCrateService, AsusAppService, ASUSOptimization, LightingService | Unverified names. Running with observed ownership needs a service-state read (Startup module has service scanning). |
| SignalRGB installed, running | Uninstall key, process name; winget `WhirlwindFX.SignalRgb` is in `catalog.json` | Catalog entry present; process name unverified. |
| HWiNFO installed | `HKCU\Software\HWiNFO64` or `HWiNFO32` | Present: `CapabilityDetector.DetectHwInfo`. |
| Sensor backend | LibreHardwareMonitorLib plus PawnIO driver | Not integrated. No package added in this batch. |

"Observed ownership" is only set when detection can show the program driving the domain: OpenRGB server enumerating devices, FanControl with a loaded configuration, Armoury Crate services running on an ASUS platform. Without that, running programs stay advisory. Do not mark ownership from installation or a running process alone.

Detection must record a `NotInstalled` observation for every companion it looked for and found absent. Leaving a companion out means "not checked" and the tab stays Unknown with no Install offer.

## App integration still needed (batch 2, App)

1. A session-scoped `IHardwareFactsProvider` in `App/Services` (or an Interop project) that builds `ObservedHardwareFacts` from the sources above, cached, refreshed by the page refresh button and on resume.
2. Settings, Advanced: an off-by-default "Show all hardware controls" switch bound to `HardwareCompatibilityOptions.ShowAllControls`. Copy must say it does not enable writes.
3. Each Hardware tab view model calls `HardwareCompatibilityPolicy.Decide` and renders: a status banner with `Explanation`, an expandable details block with `Evidence` and `ConflictNotes`, and the `Action` button. Controls render when `ControlsVisible`; every write path checks `Operations` for WriteDevices first (or `LiveWritesAllowed`), sensor reads check ReadSensors, and companion buttons check InstallCompanion or OpenCompanion, so the override can never reach hardware.
4. Install actions go through the Software module's pending-actions queue (one-way, no fabricated before-state). Open actions launch the companion as the signed-in desktop user through `IInteractiveUserContext`, never from the elevated token.
5. Tabs remain in the sidebar regardless of availability. A Conflict or Unavailable tab shows the explanation in place of controls.
6. The Home tab's "Hardware ecosystems" list can keep reading `CapabilityDetector`; it does not need the policy.

## Pending verification (owner or a real machine)

- G-Helper on Sam's Strix laptop: confirm it controls the machine (not merely that it starts), then add that machine's exact SystemProductName to `GHelperSupportCatalog.VerifiedModels`. Until an entry exists, no ASUS laptop gets an Install offer, and a running G-Helper only unlocks Open.
- G-Helper's supported-model claims (its README lists ROG, TUF, ProArt, Zenbook and Vivobook families): unverified, not encoded. If a family rule is ever added, it must cite the pinned G-Helper release.
- Vendor alternatives for non-ASUS laptops (OmenMon for HP was named in earlier notes): none offered until support is checked.
- FanControl on laptops: whether it can drive EC fans on the ASUS laptop is unverified; the policy still offers it and lets the person decide.
- Armoury Crate coexistence with G-Helper: G-Helper's own guidance about Armoury Crate services is the reason it sits in the likely-interferer list. Unverified link: https://github.com/seerge/g-helper (unverified).
- OpenRGB SDK default port and "close other RGB software" guidance: https://openrgb.org/ (catalog link) and the OpenRGB wiki (unverified).

## Non-goals in this batch

No live detection, no process launching, no installs, no packages, no App or Owner Mode edits, no compatibility tables beyond the rules above.
