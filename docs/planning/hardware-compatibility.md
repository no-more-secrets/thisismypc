# Hardware compatibility: facts and decisions

Shared hardware detection from [v1-completion-plan.md](v1-completion-plan.md) section 5. Batch 1 (2026-09-06) is the pure policy with a fake-fact test matrix. Batch 2 (2026-09-09) is live detection and the four companion tabs; see "Detection as built" and "App integration as built" below. Nothing in the policy probes hardware, launches programs or writes anything; detection reads, and the tabs write nothing to hardware in this batch.

## What exists (Core, `src/ThisIsMyPC.Core/Hardware/`)

| Type | Role |
| --- | --- |
| `ObservedHardwareFacts` | Input record: identity, form-factor evidence, companion observations, ATKACPI presence, the built-in lighting controllers' device list, OpenRGB server state (ownership evidence only), sensor backend state. Every optional field means "not observed" when null. |
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

**Lighting (built-in controllers, [lighting-controllers.md](../lighting-controllers.md)).** No companion: the tab drives devices itself or not at all, so it never offers Install or Open. `LightingDevices` null (detection did not run): Unknown, no writes. Empty (detection ran, no supported device answered): Unavailable, no action. One or more: Available with WriteDevices; the explanation names the devices and the evidence lists each with its controller family and location. Laptops are not excluded. OpenRGB is a likely interferer: running with its SDK server serving devices it owns Lighting and the tab is Conflict; running without devices adds an advisory note. The device cards are the controls; an Available Lighting tab shows no button.

**Cooling (FanControl).** Not checked: Unknown. Installed (running or not): Available with Open only; the copy says "open it to manage fan curves" and never claims FanControl owns anything. A running process is not ownership; a loaded configuration is a separate observation and appears only as an evidence line. Confirmed not installed: Unavailable with Install. On an ASUS laptop the evidence notes that fan modes also live in G-Helper. ThisIsMyPC opens FanControl; it never drives its configuration (backlog: opaque JSON, banned shell-out).

**Monitoring (LibreHardwareMonitor).** Follows `SensorBackendState`: NotIntegrated gives PendingVerification (the state this build is in), DriverMissing gives Unavailable, Ready gives Available with ReadSensors only. HWiNFO or LibreHardwareMonitor running as apps are never conflicts.

**Conflicts, applied uniformly after the per-tab rule.** A running companion other than the tab's own backend whose observed ownership includes the domain turns the tab into Conflict: no action, no operations, explanation names the program. A running "likely interferer" without observed ownership adds an advisory conflict note and changes nothing else. Installed-but-not-running programs only add an evidence line; unobserved programs add nothing. Likely interferers per domain: System Control: Armoury Crate. Lighting: SignalRGB, Armoury Crate, G-Helper. Cooling: G-Helper, Armoury Crate. Monitoring: none.

**Debug visibility override.** `HardwareCompatibilityOptions.ShowAllControls` sets `ControlsVisible` true on every tab and nothing else. A theory over ten scenarios (conflict, available, unknown device count, invalid device count, pending laptop, installed FanControl, ready sensors, role-only laptop guess, unobserved, empty) asserts availability, operations, conflicts, evidence, actions, explanation and backend are identical with the override on and off.

## Test matrix

`tests/ThisIsMyPC.Core.Tests/Hardware/`: 125 tests, all fake facts, no live reads. The builder gives every machine a confirmed NotInstalled for each companion it does not name (detection ran); `Unobserved(facts)` models detection never running.

```
dotnet test tests/ThisIsMyPC.Core.Tests --filter "FullyQualifiedName~ThisIsMyPC.Core.Tests.Hardware"
```

## Detection as built (batch 2, 2026-09-09)

Two layers. The shared inventory ([hardware-detection.md](../hardware-detection.md), `IHardwareDetectionService`) reads firmware, present devices, platform role, battery and the ATKACPI interface, and records companions it happens to see. The module layer in `Core/Hardware/Detection/` starts from that snapshot and adds what the tabs need; `Interop.Win32/Hardware/` holds its native reads.

| Type | Role |
| --- | --- |
| `IHardwareProbeEnvironment` | The module layer's machine reads: known folders, file and directory checks, running-process paths, the OpenRGB SDK probe. `Win32HardwareProbeEnvironment` implements it; tests script it. |
| `ILightingBackend` (Core/Hardware/Lighting) | The built-in controllers: `DetectAsync` (cached, rescan on refresh) supplies `LightingDevices`; `OpenAsync` gives the page its session. `NativeLightingBackend` in `src/ThisIsMyPC.Lighting` implements it. |
| `OpenRgbSdkProtocol` | Packet header codec and the request builders (controller count, controller data, protocol version, client name). Constants from NetworkProtocol.h. |
| `CompanionDetector` | One `CompanionDetection` (observation, launch path, notes) per companion. Every companion gets an observation, so an absent one is `NotInstalled`, never unobserved; the shared inventory's positive-only list is replaced, not merged. |
| `HardwareFactsProvider` | `IHardwareFactsProvider`: takes the shared snapshot (identity, chassis types, platform role, battery, ATKACPI pass through), adds the internal-panel fact, the companions from the detector, the lighting backend's device list (a refresh rescans), and the OpenRGB probe only while OpenRGB runs. One pass at a time, cached until a refresh (a refresh also refreshes the shared inventory), `Changed` after each pass. A failed inventory read or lighting pass degrades to a missing fact with a note. Sensor backend stays `NotIntegrated`. |
| `HardwareDetectionSnapshot` | Facts, the shared `HardwareSnapshot` they came from, launch paths, detection notes and the pass time. |

Process paths are read with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` and `QueryFullProcessImageNameW`, which works from the unelevated UI against an elevated companion (FanControl runs elevated from its task). A process whose path is still unreadable counts as running with no launch path.

Where each companion is looked for, and what sets ownership:

| Companion | Installed when any of | Running when | Launch path order | Ownership |
| --- | --- | --- | --- | --- |
| OpenRGB | uninstall entry starting "OpenRGB"; `Program Files\OpenRGB\OpenRGB.exe`; `%LocalAppData%\Programs\OpenRGB`; winget portable `OpenRGB.OpenRGB*`; `%AppData%\OpenRGB` folder (has run) | process `OpenRGB` | running process, DisplayIcon, InstallLocation, known folders | Lighting when running and the SDK server answers with a count above zero |
| FanControl | scheduled task `\FanControl` (Command resolved against Start In); uninstall entry; `FanControl*` under Program Files, Program Files (x86), `%LocalAppData%\Programs`; winget portable `Rem0o.FanControl*` | process `FanControl` | running process, task, uninstall entry, folders | Cooling when running and a `Configurations\*.json` sits next to the exe (Sam's v226 layout; not a schema guarantee) |
| G-Helper | HKCU Run value `GHelper`; `%AppData%\GHelper\config.json`; winget portable `seerge.g-helper*`; winget Links `GHelper.exe` | process `GHelper` | running process, Run value, winget | never |
| Armoury Crate | uninstall entry containing "Armoury Crate"; any of the services ArmouryCrateService, ArmouryCrateControlInterface, AsusAppService, ASUSOptimization, LightingService | any of those services Running | none (Store app) | Lighting when LightingService runs; System Control and Cooling when ArmouryCrateService runs and ATKACPI is present |
| SignalRGB | uninstall entry starting "SignalRGB" | process `SignalRgb` or `SignalRgbLauncher` | running process, DisplayIcon, InstallLocation | never |
| LibreHardwareMonitor | uninstall entry; `Program Files\LibreHardwareMonitor`; winget portable | process `LibreHardwareMonitor` | running process, entry, folders | never |
| HWiNFO | uninstall entry starting "HWiNFO"; `HKCU\Software\HWiNFO64` or `HWiNFO32` | process `HWiNFO64` or `HWiNFO32` | running process, DisplayIcon, InstallLocation | never |

Verified live on Sam's desktop (2026-09-09): chassis type 3, platform role Desktop, no battery, ATKACPI absent. FanControl was found by process and task with a saved configuration. OpenRGB was found by uninstall entry and Program Files, not running. HWiNFO was found by entry and key. Unverified: every G-Helper, Armoury Crate and SignalRGB source (no such machine at hand), and the SDK probe against a running OpenRGB.

DisplayIcon counts as a launch path only when its file name is the program's own executable. Installers often point it at the uninstaller. The shared inventory's positive sightings are merged in: a companion it saw running or registered stays running or installed here, with this layer's launch path and ownership.

## App integration as built (batch 2)

- `Modules.Hardware`: `HardwareCompanionModule` (one class per tab: System Control, Lighting, Cooling, Monitoring) returns `HardwareTabScanData` (the tab's decision, the full report, the launch path, the notes). `CheckAvailabilityAsync` is always true, so the tabs stay in the sidebar. `ScanSystemStateAsync` returns the cached snapshot; `RefreshAsync` runs a fresh pass. `ShowAllControls` is read from Settings on every evaluation.
- `App/ViewModels/HardwareTabViewModel` + `Views/HardwareTabView`: status badge (Available, Not available, Unknown, Not verified, In use elsewhere), the explanation, conflict notes, and the companion button. A controls card appears only when `ControlsVisible`, with a warning line when the override is the reason. A closed Details expander holds the evidence. The page opens on the cached snapshot. A cache older than 30 seconds is re-checked behind the page; the Refresh button forces a fresh pass.
- `App/Services/HardwareCompanionActions`: Install stages `install:{catalogId}` on `IPendingActionsService` through `SoftwareActionFactory`, applied from the review panel like any install. Open calls `IInteractiveUserContext.LaunchAsUser` on the detected path; unelevated, that goes through the shell so an administrator-manifested program gets its own UAC prompt. The view model checks `Operations` (InstallCompanion, OpenCompanion) before either; the override cannot reach them. Install is disabled with a hint while the Software module reports winget unavailable, and the button follows the queue when the review panel discards or applies the action.
- Settings > Advanced: "Show all hardware controls" (`AppSettingKeys.ShowAllHardwareControls`). The copy says it does not enable writes, bypass drivers or override conflict checks.
- Catalog: `fancontrol` (Rem0o.FanControl), `g-helper` (seerge.g-helper), `librehardwaremonitor` added; ids confirmed with `winget search` on 2026-09-09.
- Lighting device controls (2026-09-10): `Core/Hardware/Lighting/` holds the models and the `ILightingSession` and `ILightingBackend` contracts (plus the OpenRGB SDK codec and `OpenRgbSdkClient`, kept for diagnostics). `src/ThisIsMyPC.Lighting` holds the built-in controllers ([lighting-controllers.md](../lighting-controllers.md)); `Interop.Win32/Hardware/Hid` and `I2c` are its transports. `App/ViewModels/LightingControlsViewModel` renders one card per device: mode, brightness, speed, direction, random colors, one color per mode color or per zone, and Save where supported. Writes coalesce latest-wins. Every write asks the tab's live decision for WriteDevices first.
- Not built yet: sensor backend integration (Monitoring stays PendingVerification), Cooling presets, saved lighting presets, the tray flyout contributions.

## Detection sources (planning table, kept for the unverified rows)

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
| OpenRGB running, SDK reachable, device count | Process name plus TCP connect to the SDK server, then the SDK controller-count request | Built and verified against OpenRGB 1.0rc3.1 on 2026-09-10. Now ownership evidence only: a serving OpenRGB makes Lighting a Conflict. |
| Lighting devices | HID enumeration (hid.dll) and GPU I2C (NvAPI) through the built-in controllers' detectors | Built 2026-09-10; verified on Sam's desktop (Glorious Model O, RTX 4080 Strix). |
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

## App integration rules (batch 2, all built)

1. A session-scoped `IHardwareFactsProvider` builds `ObservedHardwareFacts` from the sources above, cached, refreshed by the page refresh button. Refresh on resume is not wired yet.
2. Settings, Advanced: an off-by-default "Show all hardware controls" switch bound to `HardwareCompatibilityOptions.ShowAllControls`. The copy says it does not enable writes.
3. Each Hardware tab renders a status banner with `Explanation`, a closed details block with `Evidence` and `ConflictNotes`, and the `Action` button. Controls render when `ControlsVisible`; every write path checks `Operations` for WriteDevices first (or `LiveWritesAllowed`), sensor reads check ReadSensors, and companion buttons check InstallCompanion or OpenCompanion, so the override can never reach hardware.
4. Install actions go through the Software module's pending-actions queue (one-way, no fabricated before-state). Open actions launch the companion as the signed-in desktop user through `IInteractiveUserContext`, never from the elevated token.
5. Tabs remain in the sidebar regardless of availability. A Conflict or Unavailable tab shows the explanation in place of controls.
6. The Home tab's "Hardware ecosystems" list keeps reading `CapabilityDetector`; it does not need the policy.

## Pending verification (owner or a real machine)

- G-Helper on Sam's Strix laptop: confirm it controls the machine (not merely that it starts), then add that machine's exact SystemProductName to `GHelperSupportCatalog.VerifiedModels`. Until an entry exists, no ASUS laptop gets an Install offer, and a running G-Helper only unlocks Open.
- G-Helper's supported-model claims (its README lists ROG, TUF, ProArt, Zenbook and Vivobook families): unverified, not encoded. If a family rule is ever added, it must cite the pinned G-Helper release.
- Vendor alternatives for non-ASUS laptops (OmenMon for HP was named in earlier notes): none offered until support is checked.
- FanControl on laptops: whether it can drive EC fans on the ASUS laptop is unverified; the policy still offers it and lets the person decide.
- Armoury Crate coexistence with G-Helper: G-Helper's own guidance about Armoury Crate services is the reason it sits in the likely-interferer list. Unverified link: https://github.com/seerge/g-helper (unverified).
- OpenRGB's "close other RGB software" guidance is why OpenRGB, SignalRGB and Armoury Crate sit in Lighting's likely-interferer list: https://openrgb.org/ (unverified).
- The release build under Code Integrity Guard with NvAPI mapped before the policy: exercised by `NvApiUnderCigTests` in the test host; the installed NativeAOT app has not been run through the GPU path yet.

## Non-goals so far

No sensor backend, no device writes, no packages added, no Owner Mode edits, no compatibility tables beyond the rules above. Install and Open are the only actions, and both run through existing app services.
