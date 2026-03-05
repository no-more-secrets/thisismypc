# ThisIsMyPC — Project State

## Current Status
BMAD Method v6.0.4 installed. Two research reports completed. No code written yet — still in planning/research phase.

## BMAD Setup
- Installed at `_bmad/` (bmm module, claude-code tool)
- Config: `_bmad/bmm/config.yaml`
- Output artifacts: `_bmad-output/planning-artifacts/`
- Known issue: Global BMAD commands at `C:\Users\user\.claude\commands\bmad*` cause a duplicate warning. Safe to delete those globals.

## Research Completed

### 1. Domain Research
`_bmad-output/planning-artifacts/research/domain-low-level-windows-system-control-research-2026-03-05.md`
- Windows system control ecosystem survey
- Competitive landscape (Autoruns, HWiNFO, G-Helper, OpenRGB, NirSoft deep dives)
- Driver signing, HVCI, Vulnerable Driver Blocklist
- **Conclusion:** Driver-free MVP is fully viable using Win32, WMI, ETW, COM, SetupAPI, DXVA2

### 2. Technical Research — Windows Registry & System Locations
`_bmad-output/planning-artifacts/research/technical-windows-registry-system-locations-research-2026-03-05.md`

In-progress (steps 1–2 of 6 complete). Step 3 (integration patterns) is next.

#### Key findings by module:

**Startup & Services Manager**
- Run keys: `HKLM/HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run|RunOnce` (+ WOW6432Node mirrors)
- Policy run: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run`
- Winlogon chain: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` (`Userinit`, `Shell`)
- Services: `HKLM\SYSTEM\CurrentControlSet\Services\<Name>` — use SCM API for live state
- Scheduled tasks registry: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tasks|Tree`
- Scheduled tasks files: `%SystemRoot%\System32\Tasks\` — access via `ITaskService` COM
- Startup folders: `%APPDATA%\...\Startup` (user), `%ProgramData%\...\StartUp` (all users)

**Display Control (DDC/CI)**
- EDID: `HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\<ID>\<Instance>\Device Parameters\EDID` — access via SetupAPI, not raw registry
- Display adapters: `HKLM\SYSTEM\CurrentControlSet\Control\Video\{GUID}\0000`
- APIs: `dxva2.dll` — `GetPhysicalMonitorsFromHMONITOR`, `SetMonitorBrightness`, `SetVCPFeature`

**Hardware Sensors (System Info Dashboard)**
- WMI: `root\CIMV2` (Win32_Processor, Win32_VideoController, etc.), `root\WMI` (MSAcpi_ThermalZoneTemperature)
- HWiNFO shared memory: `Global\HWiNFO_SENS_SM2` (mutex: `Global\HWiNFO_SM2_MUTEX`) — no driver needed
- CPU info: `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0`

**ASUS WMI/ACPI Platform Tuning**
- Device path: `\\.\ATKACPI` via `CreateFile` + `DeviceIoControl`
- IOCTL: `0x0022240C`
- WMI namespace: `root\WMI`, classes `AsusAtkWmi_WMNB` (DSTS/DEVS methods), `AsusAtkWmi_WMBC` (battery)
- Key method IDs: fan mode `0x00110005`, GPU MUX `0x00090016`, battery limit `0x00120057`, boost `0x00110019`
- Driver service: `HKLM\SYSTEM\CurrentControlSet\Services\ATKACPI`

**RGB / HID Devices**
- HID tree: `HKLM\SYSTEM\CurrentControlSet\Enum\HID\VID_XXXX&PID_XXXX\`
- USB tree: `HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_XXXX&PID_XXXX\`
- USB flags: `HKLM\SYSTEM\CurrentControlSet\Control\usbflags\VVVVPPPPRRR`
- Enumerate via SetupAPI (`SetupDiGetClassDevs`), not raw registry
- OpenRGB integration: SDK client on TCP 6742 (don't reimplement detection)

**Power Plans**
- Plans: `HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\`
- Settings: `HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\`
- Active plan: `...\PowerSchemes\ActivePowerScheme` (REG_SZ GUID)
- Key GUIDs: Balanced `381b4222-...`, High Perf `8c5e7fda-...`, Ultimate `e9a42b02-...`
- Use `powrprof.dll` API (`PowerGetActiveScheme`, `PowerSetActiveScheme`) over direct registry writes

**Shell & Explorer Customization (ExplorerPatcher scope)**
- ExplorerPatcher config: `HKCU\Software\ExplorerPatcher`
- Explorer prefs: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` (`TaskbarAl`, etc.)
- Explorer policies: `HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer`
- Themes: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`
- Context menu handlers: `HKCR\*\shellex\ContextMenuHandlers\{CLSID}`, `HKCR\Directory\shellex\...`
- COM server registration: `HKCR\CLSID\{CLSID}\InprocServer32`
- Shell ext approval: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved`
- AppInit_DLLs (DLL injection): `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows` — disabled under HVCI

**Cross-cutting: Key system directories**

| Path | Purpose |
|------|---------|
| `%SystemRoot%\System32\drivers\` | Kernel drivers (.sys) |
| `%SystemRoot%\Inf\` | Driver INFs; SetupAPI logs |
| `%SystemRoot%\System32\Tasks\` | Scheduled task XML |
| `%SystemRoot%\System32\wbem\` | WMI repository |
| `%USERPROFILE%\NTUSER.DAT` | HKCU hive file |

## Next Steps
- [ ] Complete technical research steps 3–6 (integration patterns, architecture, performance, synthesis)
- [ ] Create PRD (`/bmad-bmm-create-prd`)
- [ ] Create architecture doc (`/bmad-bmm-create-architecture`)
- [ ] Begin implementation planning
