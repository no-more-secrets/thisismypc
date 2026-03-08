# Autoruns Export Analysis for Epic 3

**ThisIsMyPC -- Startup, Services & Scheduled Tasks**
**Analyzed 2026-03-07 -- Source: Sysinternals Autoruns on Windows 11 25H2**

---

## Overview

This document analyzes a 1,846-entry Autoruns CSV export to identify scan targets, data model requirements, and UX expectations for Epic 3 stories. The export covers 15 Autoruns categories across system-wide and per-user profiles.

### Category Distribution

| Category | Entries | Epic 3 Story | Scope |
|---|---|---|---|
| Drivers | 462 | Out of scope | -- |
| Services | 327 | **Story 3.3** | In scope |
| Tasks | 278 | **Story 3.4** | In scope |
| Codecs | 243 | Out of scope | -- |
| Explorer | 147 | Context: Story 3.1/3.2 (shell extensions already in Epic 2) | Partial |
| Logon | 71 (52 unique) | **Story 3.1 + 3.2** | In scope |
| Winlogon | 80 | Out of scope (credential providers, GP extensions) | -- |
| Known DLLs | 76 | Out of scope | -- |
| Network Providers | 42 | Out of scope | -- |
| Internet Explorer | 14 | Out of scope | -- |
| Office Addins | 11 | Out of scope | -- |
| Print Monitors | 11 | Out of scope | -- |
| LSA Providers | 3 | Out of scope | -- |
| Hijacks | 1 | Out of scope | -- |
| Boot Execute | 1 | Out of scope | -- |

**Note on counts:** The Logon category has 71 raw entries but only 52 unique (location, entry) pairs. Autoruns scans both system-wide and per-user profiles, so HKCU entries appear twice. The analysis below uses deduplicated counts.

---

## Story 3.1 + 3.2: Startup Entry Scanner & Management

### Registry Locations to Scan (Logon Category)

These are the entry locations from the Autoruns Logon category. They are the primary scan targets for the startup entry scanner.

#### User-Scope Startup (HKCU) -- No Admin Required

| Registry Path | Entries | Description |
|---|---|---|
| `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | 13 | Per-user Run key -- most user-installed apps register here |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Startup` | 4 | Per-user Startup folder (shortcuts) |

**Startup folder physical path:** `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`

#### Machine-Scope Startup (HKLM) -- Admin Required to Modify

| Registry Path | Entries | Description |
|---|---|---|
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | 1 | Machine-wide Run key |
| `HKLM\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run` | 13 | 32-bit compat Run key (often used by installers) |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Common Startup` | 5 | All-users Startup folder (shortcuts) |
| `HKLM\SOFTWARE\Microsoft\Active Setup\Installed Components` | 8 | Active Setup -- runs once per user at first logon |
| `HKLM\SOFTWARE\Wow6432Node\Microsoft\Active Setup\Installed Components` | 2 | 32-bit Active Setup |

**Common Startup folder physical path:** `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup`

#### System-Level Logon (HKLM) -- Informational / Read-Only in UI

These are system-critical entries that should be displayed as read-only information:

| Registry Path | Entries | Description |
|---|---|---|
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell` | 1 | Windows shell (explorer.exe) |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit` | 1 | User initialization (userinit.exe) |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\VmApplet` | 1 | Virtual memory applet |
| `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\AlternateShell` | 1 | Safe mode shell (cmd.exe) |
| `HKLM\System\CurrentControlSet\Control\Terminal Server\Wds\rdpwd\StartupPrograms` | 1 | RDP startup (rdpclip) |
| `HKLM\Software\Microsoft\Windows NT\CurrentVersion\Windows\IconServiceLib` | 1 | Icon codec service DLL |

### Microsoft vs Third-Party Split (Logon)

| Classification | Count | Percentage |
|---|---|---|
| Microsoft | 17 | 33% |
| Third-party | 28 | 54% |
| Unknown (no company) | 7 | 13% |

**UX implication:** On a typical power-user workstation, users will see roughly 2:1 third-party to Microsoft entries. The majority of actionable items (things users might want to disable) are third-party.

### Enabled vs Disabled Distribution (Logon)

| State | Count | Percentage |
|---|---|---|
| Enabled | 17 | 33% |
| Disabled | 35 | 67% |

**UX implication:** On this machine, the user has already disabled 2/3 of startup entries. This confirms that startup management is an actively used feature. The UI should make disabled entries clearly visible (not hidden) since users want to see what they have turned off and may want to re-enable items.

### How Autoruns Disables Startup Folder Shortcuts

Autoruns disables startup folder items by moving the `.lnk` file to an `AutorunsDisabled` subfolder:
- User: `%APPDATA%\...\Startup\AutorunsDisabled\<shortcut>.lnk`
- Machine: `%ProgramData%\...\Startup\AutorunsDisabled\<shortcut>.lnk`

ThisIsMyPC should use the same convention for compatibility with Autoruns. The scanner should also check the `AutorunsDisabled` subfolder to show disabled startup folder items.

### Representative Third-Party Startup Entries

From this export, common third-party startup entries include:

| Entry | Company | Location | State |
|---|---|---|---|
| Steam | Valve Corporation | HKCU Run | enabled |
| OneDrive | Microsoft Corporation | HKCU Run | enabled |
| Voicemeeter Potato | VB-AUDIO Software | User Startup folder | enabled |
| Discord | GitHub (Squirrel updater) | HKCU Run | disabled |
| Adobe Creative Cloud | Adobe Inc. | HKLM Wow6432Node Run | disabled |
| Docker Desktop | Docker Inc. | HKCU Run | disabled |
| LGHUB | Logitech, Inc. | HKCU Run | disabled |
| Glorious Core | Glorious, LLC | HKLM Wow6432Node Run | disabled |
| GlobalProtect | Palo Alto Networks | HKLM Wow6432Node Run | disabled |
| Tailscale | Tailscale Inc. | Common Startup folder | disabled |

---

## Story 3.3: Windows Services Management

### Registry Location

All 327 service entries use a single registry path:

```
HKLM\System\CurrentControlSet\Services\<ServiceName>
```

Each service subkey contains values for:
- `Start` (DWORD) -- startup type: 0=Boot, 1=System, 2=Automatic, 3=Manual, 4=Disabled
- `Type` (DWORD) -- service type
- `ImagePath` (REG_EXPAND_SZ) -- executable path or svchost command line
- `DisplayName` (REG_SZ) -- human-readable name
- `Description` (REG_SZ) -- service description
- `ObjectName` (REG_SZ) -- account the service runs under

**Note:** While the registry path is the underlying storage, the recommended API for service management is the Service Control Manager (SCM) via `OpenSCManager`/`OpenService`/`ChangeServiceConfig`. Direct registry writes are not recommended for services.

### Microsoft vs Third-Party Split (Services)

| Classification | Count | Percentage |
|---|---|---|
| Microsoft | 296 | 91% |
| Third-party | 29 | 9% |
| Unknown (no company) | 2 | <1% |

**UX implication:** Users will see a heavily Microsoft-dominated list. The UI should provide filtering/grouping by company and perhaps a "third-party only" filter to help users find the services they are most likely to want to manage.

### Enabled vs Disabled Distribution (Services)

| State | Count | Percentage |
|---|---|---|
| Enabled | 296 | 91% |
| Disabled | 31 | 9% |

**UX implication:** Unlike Logon entries, most services are enabled. All 31 disabled services are third-party (the user has disabled all non-essential third-party services). Zero Microsoft services are disabled.

### Third-Party Services (Complete List)

| Service Name | Company | State | Description |
|---|---|---|---|
| AdobeUpdateService | Adobe Inc. | disabled | Creative Cloud Update Service |
| AdskLicensingService | Autodesk, Inc. | disabled | Autodesk Desktop Licensing Service |
| Autodesk Access Service Host | Autodesk, Inc. | disabled | Host process for Autodesk product access services |
| com.docker.service | Docker Inc. | disabled | Docker Desktop Service |
| EpicGamesUpdater | Epic Games, Inc. | disabled | Epic Games Launcher updater |
| FlexNet Licensing Service 64 | Flexera | disabled | Licensing service for various software |
| Futuremark SystemInfo Service | Futuremark | disabled | SystemInfo Service (benchmarking) |
| FvSvc | NVIDIA | **enabled** | NVIDIA FrameView SDK service |
| GameInputRedistService | Windows (R) Win 7 DDK provider | **enabled** | GameInput Redist Service |
| GoogleChromeElevationService | Google LLC | disabled | Chrome elevation/update service |
| GooglePlayGamesServices | Google LLC | disabled | Google Play Games Services |
| GoogleUpdaterInternalService | Google LLC | disabled | Google Updater internal service |
| GoogleUpdaterService | Google LLC | disabled | Google Updater service |
| JetBrainsEtwHost.16 | JetBrains s.r.o | disabled | ETW event collector for JetBrains tools |
| KeyAccess | Sassafras Software Inc. | disabled | KeyServer Client service |
| LGHUBUpdaterService | Logitech, Inc. | disabled | LGHUB Updater |
| NTKDaemonService | Native Instruments GmbH | disabled | NTKDaemon for audio |
| NvContainerLocalSystem | NVIDIA Corporation | **enabled** | NVIDIA LocalSystem Container |
| NVDisplay.ContainerLocalSystem | NVIDIA Corporation | **enabled** | NVIDIA Display Container |
| OverwolfUpdater | Overwolf LTD | disabled | Overwolf Updater |
| PaceLicenseDServices | PACE Anti-Piracy, Inc. | disabled | PACE Licensing Technology |
| PanGPS | Palo Alto Networks | disabled | GlobalProtect VPN |
| Plastic Server 6 | plasticd | disabled | Plastic SCM Server |
| Red Giant Service | Maxon Computer GmbH | disabled | Red Giant/Maxon services |
| Rockstar Service | Rockstar Games | disabled | Rockstar Game Library integrity |
| Steam Client Service | Valve Corporation | **enabled** | Steam content update service |
| Tailscale | Tailscale Inc. | disabled | Tailscale VPN |
| ucldr_battlegrounds_gl | Wellbia.com Co., Ltd. | disabled | Anti-cheat (PUBG) |
| zksvc | KRAFTON, Inc | disabled | Anti-cheat service (PUBG) |

Only 4 third-party services remain enabled: NVIDIA (2), Steam (1), and GameInput (1). All others have been manually disabled.

---

## Story 3.4: Scheduled Task Auditing

### Entry Location

All 278 task entries use:

```
Entry Location: Task Scheduler
```

Tasks are not stored in the registry. They are managed through the Task Scheduler API (`ITaskService` COM interface) or the `schtasks.exe` command-line tool. Task definitions are stored as XML files in:

```
%SystemRoot%\System32\Tasks\<TaskPath>
```

### Task Path Distribution

| Path Prefix | Count | Notes |
|---|---|---|
| `\Microsoft\Windows\*` | 236 | Built-in Windows maintenance tasks |
| `\Microsoft\Office\*` | 9 | Office update/maintenance tasks |
| `\Microsoft\VisualStudio\*` | 2 | Visual Studio telemetry |
| `\Microsoft\XblGameSave\*` | 1 | Xbox game save sync |
| Root-level (`\TaskName`) | 27 | Third-party and user-created tasks |
| `\GoogleSystem\*` | 1 | Google updater |
| `\PowerToys\*` | 1 | PowerToys autorun |
| `\Sassafras\*` | 2 | License management |

### Microsoft/Windows Task Sub-Categories (236 tasks)

The 236 `\Microsoft\Windows\*` tasks span 101 sub-folders. The largest are:

| Sub-folder | Count | Purpose |
|---|---|---|
| DeviceDirectoryClient | 12 | Device registration |
| UpdateOrchestrator | 12 | Windows Update orchestration |
| input | 9 | Input methods |
| Flighting | 8 | Windows Insider flighting |
| Shell | 8 | Shell maintenance |
| Application Experience | 7 | App compatibility telemetry |
| Management | 7 | Device management |
| CertificateServicesClient | 6 | Certificate auto-enrollment |
| InstallService | 6 | App install service |

### Microsoft vs Third-Party Split (Tasks)

| Classification | Count | Percentage |
|---|---|---|
| Microsoft | 249 | 90% |
| Third-party | 16 | 6% |
| Unknown (no company) | 13 | 4% |

### Enabled vs Disabled Distribution (Tasks)

| State | Count | Percentage |
|---|---|---|
| Enabled | 203 | 73% |
| Disabled | 75 | 27% |

**Breakdown:**
- Microsoft/Windows tasks: 192 enabled, 44 disabled
- Third-party/root tasks: 11 enabled, 31 disabled

**UX implication:** Unlike services (where all Microsoft entries are enabled), a significant number of Microsoft tasks are disabled (44 out of 236 = 19%). This suggests that either the user or Windows itself has disabled certain maintenance tasks. The UI should avoid implying that all Microsoft tasks should be enabled.

### Third-Party Scheduled Tasks

| Task Name | Company | State | Purpose |
|---|---|---|---|
| Adobe Acrobat Update Task | Adobe Inc. | disabled | Acrobat updater |
| ETW Host Service Updater v16 | JetBrains s.r.o. | disabled | JetBrains ETW updater |
| Google Play Games Notifier | Google LLC | disabled | Play Games notifications |
| Launch Adobe CCXProcess | Adobe Inc. | disabled | Creative Cloud experience |
| MATLAB R2025b Startup Accelerator | (empty) | disabled | MATLAB prefetch |
| MicrosoftEdgeUpdateTaskMachineCore | Microsoft | disabled | Edge updater (core) |
| MicrosoftEdgeUpdateTaskMachineUA | Microsoft | disabled | Edge updater (user-agent) |
| MonitorMicroKey / MonitorMysticLight / MonitorWeatherDetector | MSI | disabled | MSI hardware monitoring |
| NvBroadcast | (empty) | disabled | NVIDIA Broadcast auto-start |
| NVIDIA App SelfUpdate | NVIDIA Corporation | **enabled** | NVIDIA App updater |
| openrgb / OpenRGB-Startup-Task | (empty) | enabled/disabled | RGB control |
| OSDAppAutoStartUp | MICRO-STAR INT'L | disabled | MSI Gaming Intelligence |
| Overwolf Updater Task | Overwolf LTD | disabled | Overwolf updater |
| RTSS | (empty) | disabled | RivaTuner Statistics Server |
| start fancontrol | Remi Mercier | disabled | FanControl autostart |
| start voicemeeter | VB-AUDIO Software | **enabled** | Voicemeeter autostart |
| StartDockerOnLogon | Docker Inc. | disabled | Docker Desktop autostart |
| ZoomUpdateTaskUser | Zoom Communications | disabled | Zoom updater |
| PowerToys Autorun | (implied) | **enabled** | PowerToys autostart |

---

## Data Model Requirements

### StartupEntry (Stories 3.1/3.2)

Based on the CSV columns and the information needed for the scanner UI:

```
StartupEntry
{
    Name            : string    // "Entry" column (e.g., "Steam", "OneDrive")
    Description     : string    // "Description" column
    Company         : string    // "Company" column -- used for Microsoft vs third-party classification
    ImagePath       : string    // "Image Path" column -- path to the executable/DLL
    LaunchString    : string    // "Launch String" column -- full command line with arguments
    IsEnabled       : bool      // "Enabled" column (enabled/disabled)
    RegistryLocation: string    // "Entry Location" column -- full registry path
    Scope           : enum      // HKCU = User, HKLM = Machine (derived from RegistryLocation)
    EntryType       : enum      // Run key, Startup folder, Active Setup, Winlogon (derived)
    Version         : string    // "Version" column (optional, for display)
}
```

**Notes:**
- `ImagePath` may be "File not found: ..." for orphaned entries. The scanner should detect and flag these.
- `LaunchString` differs from `ImagePath` -- it includes arguments (e.g., `"Steam.exe" -silent`).
- Startup folder entries have `LaunchString` pointing to a `.lnk` file. The scanner needs to resolve the shortcut target.
- Hash columns (MD5, SHA-1, SHA-256) are available in Autoruns but are expensive to compute. Consider computing only on demand or for verification.

### ServiceEntry (Story 3.3)

```
ServiceEntry
{
    ServiceName     : string    // "Entry" column (registry subkey name, e.g., "AarSvc")
    DisplayName     : string    // First part of "Description" before the colon
    Description     : string    // Part of "Description" after the colon
    Company         : string    // "Company" column
    ImagePath       : string    // "Image Path" column
    LaunchString    : string    // "Launch String" column (svchost command line)
    IsEnabled       : bool      // "Enabled" column
    StartType       : enum      // Boot(0), System(1), Automatic(2), Manual(3), Disabled(4)
    ServiceType     : enum      // Win32OwnProcess, Win32ShareProcess, KernelDriver, etc.
    Version         : string    // "Version" column
    Account         : string    // Not in CSV -- from SCM ObjectName
}
```

**Notes:**
- The Autoruns "Description" field concatenates DisplayName and Description with a colon separator (e.g., `"AarSvc: Runtime for activating conversational agent applications"`). The data model should split these.
- `StartType` is not directly in the CSV "Enabled" column (which only says enabled/disabled). The actual start type (Auto/Manual/Disabled) should be read from the SCM or registry `Start` DWORD.
- Services hosted in `svchost.exe` have their actual DLL in `ImagePath` but their `LaunchString` shows the svchost command. The UI should display both.

### ScheduledTaskEntry (Story 3.4)

```
ScheduledTaskEntry
{
    TaskPath        : string    // "Entry" column (e.g., "\Microsoft\Windows\UpdateOrchestrator\...")
    TaskName        : string    // Last segment of TaskPath
    Description     : string    // "Description" column
    Company         : string    // "Company" column (often empty for tasks)
    ImagePath       : string    // "Image Path" column
    LaunchString    : string    // "Launch String" column (action command)
    IsEnabled       : bool      // "Enabled" column
    Version         : string    // "Version" column
    // Additional fields from Task Scheduler API (not in CSV):
    // LastRunTime, NextRunTime, LastResult, Triggers[], State
}
```

**Notes:**
- Tasks have hierarchical paths (e.g., `\Microsoft\Windows\UpdateOrchestrator\Schedule Scan`). The UI should support tree or grouped display.
- Many third-party tasks have empty Company fields. The UI should fall back to extracting publisher info from the executable's version resource.
- The CSV does not include trigger information, run history, or task state. These must come from the Task Scheduler COM API (`ITaskService`).

---

## Entry Locations Not Yet in Registry Research

### Comparing against existing documentation

The following registry paths appear in the Autoruns export but are NOT documented in either `epic2-registry-research.md` or `windows-settings-registry-map.md`. These need ProcMon validation or API documentation review before implementation.

#### New Paths for Story 3.1/3.2 (Startup Scanner)

| Registry Path | Category | Notes |
|---|---|---|
| `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | Logon | Primary user startup -- NOT yet in registry map |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | Logon | Primary machine startup -- NOT yet in registry map |
| `HKLM\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run` | Logon | 32-bit compat startup -- NOT yet in registry map |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Startup` | Logon | User startup folder path -- NOT yet in registry map |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Common Startup` | Logon | Machine startup folder path -- NOT yet in registry map |
| `HKLM\SOFTWARE\Microsoft\Active Setup\Installed Components` | Logon | Active Setup -- runs once per user -- NOT yet in registry map |
| `HKLM\SOFTWARE\Wow6432Node\Microsoft\Active Setup\Installed Components` | Logon | 32-bit Active Setup -- NOT yet in registry map |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell` | Logon | Shell replacement -- display read-only |
| `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit` | Logon | User init -- display read-only |

**All Logon paths are new to the registry documentation.** This is expected since Epic 2 focused on Explorer/shell customization, not startup management.

#### New Path for Story 3.3 (Services)

| Registry Path | Category | Notes |
|---|---|---|
| `HKLM\System\CurrentControlSet\Services` | Services | Service definitions -- prefer SCM API over direct registry |

**Note:** While `HKLM\System\CurrentControlSet\Services` appears in `epic2-registry-research.md` as the parent for environment variables (`Session Manager\Environment`), the Services subtree itself is not documented for service management.

#### Paths for Story 3.4 (Scheduled Tasks)

No registry paths needed -- Task Scheduler uses the COM API (`ITaskService`) and XML files in `%SystemRoot%\System32\Tasks\`.

#### Explorer Paths Already Documented

The following Explorer category paths from Autoruns are **already documented** in `epic2-registry-research.md`:

- `HKLM\Software\Classes\*\ShellEx\ContextMenuHandlers` -- documented in Story 2.2
- `HKLM\Software\Classes\Directory\ShellEx\ContextMenuHandlers` -- documented in Story 2.2
- `HKLM\Software\Classes\Directory\Background\ShellEx\ContextMenuHandlers` -- documented in Story 2.2
- `HKLM\Software\Classes\AllFileSystemObjects\ShellEx\ContextMenuHandlers` -- documented in Story 2.2
- `HKLM\Software\Classes\Folder\ShellEx\ContextMenuHandlers` -- documented in Story 2.2
- `HKLM\Software\Classes\Drive\ShellEx\ContextMenuHandlers` -- documented in Story 2.2

#### Explorer Paths NOT Yet Documented (Lower Priority)

These are Explorer extension paths found in Autoruns that are not in the existing docs. They are lower priority (not core to Epic 3) but should be noted:

| Registry Path | Entries | Notes |
|---|---|---|
| `HKLM\SOFTWARE\Classes\Protocols\Handler` | 23 | URL protocol handlers |
| `HKLM\SOFTWARE\Classes\Protocols\Filter` | 4 | MIME filters |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellServiceObjects` | 19 | Shell service objects (system tray, etc.) |
| `HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers` | 12 | Icon overlay handlers (OneDrive sync status, etc.) |
| `HKLM\Software\Classes\*\ShellEx\PropertySheetHandlers` | 5 | Property sheet extensions |
| `HKLM\Software\Classes\Directory\Shellex\DragDropHandlers` | 1 | Drag-drop handlers |
| `HKLM\Software\Classes\Directory\Shellex\CopyHookHandlers` | 2 | Copy hook handlers |
| `HKLM\Software\Classes\Folder\ShellEx\DragDropHandlers` | 3 | Folder drag-drop handlers |

---

## Out-of-Scope Categories (Summary)

These categories are documented here for completeness but are NOT in scope for Epic 3 implementation.

### Drivers (462 entries)

- **Location:** `HKLM\System\CurrentControlSet\Services` (same parent as services, filtered by Type)
- **Split:** 363 Microsoft (79%), 85 third-party (18%), 14 unknown (3%)
- **State:** 460 enabled, 2 disabled
- **Why out of scope:** Driver management requires kernel-level understanding and carries high risk. Disabling the wrong driver can render the system unbootable.

### Codecs (243 entries)

- **Locations:** DirectShow filter CLSIDs, `Drivers32` (legacy multimedia)
- **Split:** 238 Microsoft (98%), 3 third-party, 2 unknown
- **State:** All 243 enabled
- **Why out of scope:** Codec management is specialized; nearly all are Microsoft built-in.

### Winlogon (80 entries)

- **Locations:** Group Policy extensions (56), Credential Providers (22), Credential Provider Filters (1), PLAP Providers (1)
- **Split:** 79 Microsoft (99%), 1 third-party (Splashtop credential provider)
- **State:** All 80 enabled
- **Why out of scope:** Credential providers and GP extensions are security-sensitive. Disabling the wrong one can lock users out.

### Known DLLs (76 entries)

- **Location:** `HKLM\System\CurrentControlSet\Control\Session Manager\KnownDlls`
- **Split:** 59 Microsoft, 17 unknown (system DLLs without version info)
- **State:** All 76 enabled
- **Why out of scope:** Known DLLs is a security hardening mechanism. Modifying it is a security risk.

### Network Providers (42 entries)

- **Locations:** WinSock2 catalogs, NetworkProvider order
- **Split:** All 42 Microsoft
- **State:** All 42 enabled
- **Why out of scope:** Network stack configuration, all Microsoft.

### Internet Explorer (14 entries)

- **Locations:** Browser Helper Objects (BHOs), toolbars, URL search hooks
- **Split:** 8 Microsoft, 6 third-party
- **State:** 5 enabled, 9 disabled
- **Why out of scope:** IE is deprecated on Windows 11.

### Office Addins (11 entries)

- **Locations:** `HKLM\Software\Microsoft\Office\<App>\Addins`
- **Split:** 1 Microsoft (Skype for Business), 10 third-party (Adobe Acrobat)
- **State:** All 11 disabled
- **Why out of scope:** Office addin management is better handled within Office apps.

### Print Monitors (11 entries)

- **Location:** `HKLM\SYSTEM\CurrentControlSet\Control\Print\Monitors` and `Providers`
- **Split:** 9 Microsoft, 2 third-party
- **State:** 9 enabled, 2 disabled
- **Why out of scope:** Print subsystem management.

### Boot Execute (1 entry)

- **Location:** `HKLM\System\CurrentControlSet\Control\Session Manager\BootExecute`
- **Entry:** `autocheck autochk *` (disk check at boot)
- **Why out of scope:** Critical boot-time operation.

### LSA Providers (3 entries)

- **Locations:** SecurityProviders, Authentication Packages, Notification Packages
- **Split:** All 3 Microsoft
- **Why out of scope:** Security-sensitive authentication infrastructure.

### Hijacks (1 entry)

- **Location:** `HKLM\SOFTWARE\Classes\Htmlfile\Shell\Open\Command\(Default)`
- **Entry:** Internet Explorer (iexplore.exe)
- **Why out of scope:** Legacy browser association check.

---

## Implementation Recommendations

### Story 3.1: Startup Entry Scanner

**Priority scan locations (ordered by user-visible impact):**

1. `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` -- most user apps
2. `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` -- machine-wide apps
3. `HKLM\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run` -- 32-bit apps
4. User Startup folder (via Shell Folders registry or `Environment.GetFolderPath`)
5. Common Startup folder (via Shell Folders registry or known path)
6. `HKLM\SOFTWARE\Microsoft\Active Setup\Installed Components` -- first-logon setup

**Lower priority (read-only display):**

7. Winlogon Shell/Userinit/VmApplet -- show as system-critical, non-modifiable
8. SafeBoot AlternateShell -- informational only
9. Terminal Server StartupPrograms -- informational only

**Expected entry counts for a typical power-user machine:**
- User Run key: 10-15 entries (mix of enabled/disabled)
- Machine Run keys (incl Wow6432Node): 10-15 entries
- Startup folders: 3-8 shortcuts
- Active Setup: 8-10 entries (mostly Microsoft, rarely modified)
- **Total visible to user: ~35-50 entries**

### Story 3.2: Startup Management

**Disable mechanisms by entry type:**

| Entry Type | Disable Method | Restore Method |
|---|---|---|
| Run key (HKCU or HKLM) | Move value to `...\Run\AutorunsDisabled` subkey, or delete and store in app config | Move value back |
| Startup folder shortcut | Move `.lnk` to `AutorunsDisabled` subfolder | Move `.lnk` back |
| Active Setup | Set `IsInstalled` DWORD to `0`, or prefix StubPath | Restore original value |

**Note on Task Manager compatibility:** Windows Task Manager (Win11) also manages startup items and uses a different mechanism -- it writes to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` with a binary blob that controls enabled/disabled state. ThisIsMyPC should read (and ideally honor) this mechanism for compatibility.

**Additional scan target (not in Autoruns but relevant):**
```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder
```

These `StartupApproved` keys are how Task Manager and the Settings app disable startup items without deleting the Run key entry. The binary value's first byte indicates enabled (02) or disabled (03). **This is critical for compatibility.**

### Story 3.3: Windows Services Management

**API recommendation:** Use the Service Control Manager (SCM) API via `AdvApi32.dll` P/Invokes, not direct registry access. Key functions:
- `OpenSCManager` / `EnumServicesStatusEx` -- enumerate all services
- `OpenService` / `QueryServiceConfig` / `QueryServiceConfig2` -- get service details
- `ChangeServiceConfig` -- modify start type

**Filtering strategy for the UI:**
- Default view: Show third-party services only (29 entries on this machine)
- Toggle: Show all services (327 entries)
- Group by: Company, Start Type, Running State
- Highlight: Services with "File not found" image paths (orphaned)

**Expected entry counts:**
- Third-party services: 20-40 on a typical machine
- Microsoft services: 250-300
- Total: 280-340

### Story 3.4: Scheduled Task Auditing

**API recommendation:** Use the Task Scheduler COM API (`ITaskService`, `ITaskFolder`, `IRegisteredTask`). Access via .NET COM interop or the `Microsoft.Win32.TaskScheduler` NuGet package.

**Filtering strategy for the UI:**
- Default view: Show root-level and third-party tasks only (~30-40 entries)
- Expandable: Microsoft\Windows tasks by sub-folder
- Highlight: Tasks with missing executables, tasks that have never run, tasks with failed last result

**Expected entry counts:**
- Root-level tasks: 25-35
- Microsoft\Windows tasks: 200-250
- Microsoft\Office tasks: 5-15
- Total: 250-300

---

## Summary Table: Registry Paths for windows-settings-registry-map.md

The following paths should be added to the `windows-settings-registry-map.md` Startup & Services (Epic 3) placeholder section once ProcMon-validated:

| Path | Type | Story | Status |
|---|---|---|---|
| `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | Startup Run key | 3.1/3.2 | RESEARCHED |
| `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | Startup Run key | 3.1/3.2 | RESEARCHED |
| `HKLM\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run` | Startup Run key (32-bit) | 3.1/3.2 | RESEARCHED |
| `HKCU\...\Explorer\StartupApproved\Run` | Enabled/disabled state | 3.1/3.2 | RESEARCHED |
| `HKLM\...\Explorer\StartupApproved\Run` | Enabled/disabled state | 3.1/3.2 | RESEARCHED |
| `HKLM\SOFTWARE\Microsoft\Active Setup\Installed Components` | First-logon setup | 3.1 | RESEARCHED |
| `HKLM\System\CurrentControlSet\Services\<name>\Start` | Service start type | 3.3 | RESEARCHED |
| User/Common Startup folders | Shortcut-based startup | 3.1/3.2 | RESEARCHED |
| Task Scheduler (COM API, not registry) | Scheduled tasks | 3.4 | RESEARCHED |

**ProcMon validation needed for:**
1. The `StartupApproved` binary format -- confirm the enable/disable byte values on 25H2
2. The `Active Setup` `IsInstalled` and `StubPath` behavior
3. Service `Start` type DWORD changes via SCM vs direct registry write

---

## Phase 3: Cross-Reference Analysis

**Performed 2026-03-07 — Cross-referencing ShellExView analysis, Autoruns analysis, Epic 2 registry research, and master registry map.**

---

### 3.1 Shell Extensions Overlap: ShellExView vs Autoruns Explorer Category

#### Scope Comparison

| Tool | Shell Extension Scope | Entry Count |
|---|---|---|
| ShellExView | All 21 shell extension types (Context Menu, Property Sheet, Icon Handler, Thumbnail, Preview, etc.) | 299 total, 48 context menu |
| Autoruns Explorer | Context menu handlers, property sheet handlers, icon overlay handlers, shell service objects, drag-drop handlers, copy hook handlers, URL protocol/filter handlers | 147 entries |

ShellExView exports **all registered shell extensions** regardless of whether they are loaded. Autoruns focuses on **autostart and persistence mechanisms** -- its Explorer category captures shell extensions because they are DLLs loaded into Explorer's process, making them a form of autostart.

#### What Appears in Both

Both tools cover these shell extension types:

| Extension Type | ShellExView | Autoruns Explorer | Notes |
|---|---|---|---|
| Context Menu Handlers | 48 entries across `ContextMenuHandlers` paths | Subset -- only those under `HKLM\Software\Classes\*\ShellEx\ContextMenuHandlers`, `Directory\...`, `Folder\...`, `AllFileSystemObjects\...`, `Drive\...` | Both enumerate the same registry paths; ShellExView is more exhaustive because it also resolves per-ProgID registrations |
| Property Sheet Handlers | 17 entries | 5 entries under `*\ShellEx\PropertySheetHandlers` | ShellExView finds all; Autoruns only checks the wildcard `*` path |
| Icon Overlay Handlers | 12 entries | 12 entries under `ShellIconOverlayIdentifiers` | Full overlap -- both tools find the same set |
| Drag-Drop Handlers | 5 entries | 1 entry (Directory) + 3 entries (Folder) | ShellExView is more complete |
| Copy Hook Handlers | 2 entries | 2 entries | Full overlap |

#### What ShellExView Catches That Autoruns Misses

1. **Per-ProgID context menu handlers** -- ShellExView enumerates `HKCR\<ProgID>\shellex\ContextMenuHandlers\` for every registered ProgID (e.g., `txtfile`, `exefile`, `CompressedFolder`). Autoruns only checks the wildcard/directory/folder paths. This means ProgID-specific handlers like `CompatContextMenu` (registered under `exefile`), `CryptPKO` (under `PKOFile`), and `NvAppShExt` (under `exefile`, `lnkfile`) appear in ShellExView but may not appear in Autoruns' Explorer category.

2. **Preview handlers, thumbnail handlers, infotip handlers** -- ShellExView exports all 21 extension types. Autoruns does not track these because they are not considered autostart/persistence mechanisms.

3. **Shell Folder extensions** -- ShellExView lists 70 shell folder extensions (namespace extensions like Control Panel, Recycle Bin). Autoruns does not track these in its Explorer category.

4. **Disabled extensions** -- ShellExView shows extensions that have been disabled via the dash-prefix method. Autoruns shows disabled entries too, but its detection of the disabled state may differ for some extension types.

#### What Autoruns Catches That ShellExView Misses

1. **Shell Service Objects** -- Autoruns lists 19 entries under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellServiceObjects`. These are COM objects loaded by Explorer at startup (e.g., system tray handlers). ShellExView does not enumerate this path.

2. **URL Protocol and Filter handlers** -- Autoruns lists 23 protocol handlers and 4 MIME filters under `HKLM\SOFTWARE\Classes\Protocols\`. ShellExView does not cover these (they are not technically "shell extensions" in the IShellExtInit sense).

3. **Explicit enabled/disabled tracking** -- Autoruns tracks whether each entry is enabled or disabled and provides a toggle mechanism. ShellExView also does this, so this is actually equivalent.

#### Consistency Assessment

For the context menu handlers that both tools cover, there are **no discrepancies** in the data:

- The CLSIDs, DLL paths, and company names match between the two exports.
- Both tools agree on the disable mechanism (dash-prefix for the CLSID default value).
- Both tools correctly identify the same 5 disabled handlers on this system (Internet Shortcut, NVIDIA CPL, NvAppShExt, OpenGLShExt, PowerRename).

**Conclusion:** ShellExView is the more complete tool for context menu handler enumeration (covers per-ProgID registrations). Autoruns is the more complete tool for broader Explorer persistence points (shell service objects, protocol handlers). For Epic 2 Story 2.2, ShellExView's data is the primary reference. For Epic 3, neither tool's Explorer category adds new scan targets -- the Logon/Services/Tasks categories are what matter.

---

### 3.2 Registry Path Completeness Audit

#### All Unique Registry Paths from Both Analyses

The following is a deduplicated list of every registry path referenced in the ShellExView analysis (context-menu-handlers-analysis.md) and the Autoruns analysis (autoruns-analysis.md), checked against epic2-registry-research.md and windows-settings-registry-map.md.

##### Already Documented (in epic2-registry-research.md or windows-settings-registry-map.md)

| Registry Path | Source | Documented In |
|---|---|---|
| `HKCR\*\shellex\ContextMenuHandlers\` | ShellExView | epic2-registry-research.md (Story 2.2), registry map appendix |
| `HKCR\AllFilesystemObjects\shellex\ContextMenuHandlers\` | ShellExView | epic2-registry-research.md (Story 2.2), registry map appendix |
| `HKCR\Directory\shellex\ContextMenuHandlers\` | ShellExView | epic2-registry-research.md (Story 2.2), registry map appendix |
| `HKCR\Directory\Background\shellex\ContextMenuHandlers\` | ShellExView | epic2-registry-research.md (Story 2.2), registry map appendix |
| `HKCR\Folder\shellex\ContextMenuHandlers\` | ShellExView | epic2-registry-research.md (Story 2.2), registry map appendix |
| `HKCR\<ProgID>\shellex\ContextMenuHandlers\` | ShellExView | epic2-registry-research.md (Story 2.2), registry map appendix |
| `HKCR\CLSID\{GUID}\InprocServer32\(Default)` | ShellExView | epic2-registry-research.md (Story 2.2) |
| `HKCR\*\shell\<VerbName>\command` | epic2 doc | epic2-registry-research.md (Story 2.2, static entries) |
| `HKCU\Software\Classes\CLSID\{86ca1aa0-...}\InprocServer32` | epic2 doc | epic2-registry-research.md (Story 2.3), registry map |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | epic2 doc | epic2-registry-research.md (Stories 2.3, 2.4), registry map |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer` (`LaunchTo`) | epic2 doc | epic2-registry-research.md (Story 2.4), registry map |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer` | epic2 doc | epic2-registry-research.md (Story 2.4), registry map |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | epic2 doc | epic2-registry-research.md (Story 2.4), registry map |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement` | epic2 doc | epic2-registry-research.md (Story 2.4), registry map |
| `HKCU\Environment` | epic2 doc | epic2-registry-research.md (Story 2.5), registry map |
| `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` | epic2 doc | epic2-registry-research.md (Story 2.5), registry map |
| `HKLM\SOFTWARE\Policies\Microsoft\Dsh` | epic2 doc | epic2-registry-research.md (Story 2.3), registry map |
| `HKCU\Software\Policies\Microsoft\Windows\CloudContent` | epic2 doc | epic2-registry-research.md (Story 2.4) |
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent` | epic2 doc | epic2-registry-research.md (Story 2.4) |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings` | epic2 doc | epic2-registry-research.md (Story 2.4) |

##### NEW Paths -- Epic 3 Core (Need Adding to Registry Map)

These are the primary scan targets for Epic 3 implementation. They are documented in autoruns-analysis.md but not yet in the master registry map.

| # | Registry Path | Story | Notes | ProcMon Needed? |
|---|---|---|---|---|
| 1 | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | 3.1/3.2 | User startup Run key (13 entries in export) | Yes |
| 2 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` | 3.1/3.2 | Machine startup Run key (1 entry in export) | Yes |
| 3 | `HKLM\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run` | 3.1/3.2 | 32-bit compat Run key (13 entries) | Yes |
| 4 | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` | 3.1/3.2 | Task Manager disable state (binary blob) | Yes -- confirm byte format |
| 5 | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32` | 3.1/3.2 | 32-bit variant of StartupApproved | Yes |
| 6 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` | 3.1/3.2 | Machine-scope StartupApproved | Yes |
| 7 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32` | 3.1/3.2 | Machine-scope 32-bit StartupApproved | Yes |
| 8 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder` | 3.1/3.2 | StartupApproved for folder shortcuts | Yes |
| 9 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Startup` | 3.1/3.2 | Points to user Startup folder path | No (read-only reference) |
| 10 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Common Startup` | 3.1/3.2 | Points to common Startup folder path | No (read-only reference) |
| 11 | `HKLM\SOFTWARE\Microsoft\Active Setup\Installed Components` | 3.1 | First-logon Active Setup entries (8 entries) | Yes |
| 12 | `HKLM\SOFTWARE\Wow6432Node\Microsoft\Active Setup\Installed Components` | 3.1 | 32-bit Active Setup (2 entries) | Yes |
| 13 | `HKLM\System\CurrentControlSet\Services\<ServiceName>` | 3.3 | Service definitions (327 entries); prefer SCM API | Partial -- SCM is the API |
| 14 | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell` | 3.1 | Windows shell (explorer.exe) -- read-only display | No |
| 15 | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit` | 3.1 | User init (userinit.exe) -- read-only display | No |

##### NEW Paths -- Explorer Extensions (Lower Priority, Not Epic 3 Core)

These were discovered in the Autoruns Explorer category. They are not core to Epic 3 but should be noted for future epics.

| # | Registry Path | Entries | Notes |
|---|---|---|---|
| 16 | `HKLM\SOFTWARE\Classes\Protocols\Handler` | 23 | URL protocol handlers |
| 17 | `HKLM\SOFTWARE\Classes\Protocols\Filter` | 4 | MIME filter handlers |
| 18 | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellServiceObjects` | 19 | COM objects loaded by Explorer at startup |
| 19 | `HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers` | 12 | Icon overlay handlers |
| 20 | `HKLM\Software\Classes\*\ShellEx\PropertySheetHandlers` | 5 | Property sheet extensions |
| 21 | `HKLM\Software\Classes\Directory\Shellex\DragDropHandlers` | 1 | Drag-drop handlers |
| 22 | `HKLM\Software\Classes\Directory\Shellex\CopyHookHandlers` | 2 | Copy hook handlers |
| 23 | `HKLM\Software\Classes\Folder\ShellEx\DragDropHandlers` | 3 | Folder drag-drop handlers |

##### Additional Paths from ShellExView (Virtual Shell Classes, Lower Priority)

These are HKCR locations where only Microsoft system handlers register. Documented in the ShellExView analysis section 4 but not in the registry map.

| # | Registry Path | Handlers | Notes |
|---|---|---|---|
| 24 | `HKCR\Drive\shellex\ContextMenuHandlers\` | 7 | EPP, Portable Devices, File Locksmith, Previous Versions, Sharing, Enhanced Storage, CD Burning |
| 25 | `HKCR\DesktopBackground\shellex\ContextMenuHandlers\` | 1 | SlideshowContextMenu |
| 26 | `HKCR\Printers\shellex\ContextMenuHandlers\` | 1 | PrintUIShellExtension |
| 27 | `HKCR\LibraryFolder\shellex\ContextMenuHandlers\` | 1 | Library Folder Context Menu |
| 28 | `HKCR\LibraryFolder\background\shellex\ContextMenuHandlers\` | 2 | Sharing, New Menu |
| 29 | `HKCR\UserLibraryFolder\shellex\ContextMenuHandlers\` | 2 | Sharing, SendTo |
| 30 | `HKCR\OpenSearchProvider\shellex\ContextMenuHandlers\` | 1 | OpenSearch Result Context Menu |

##### Scheduled Tasks -- No Registry Paths

Story 3.4 (Scheduled Tasks) does not use registry paths. Tasks are managed through the Task Scheduler COM API (`ITaskService`) and stored as XML in `%SystemRoot%\System32\Tasks\`. This is correctly documented in the Autoruns analysis.

#### Summary

- **Paths already documented:** 20 (all Epic 2 paths accounted for)
- **New paths for registry map (Epic 3 core):** 15 (items 1-15 above) -- these are being added to `windows-settings-registry-map.md`
- **Lower-priority Explorer paths:** 8 (items 16-23) -- noted but not added to registry map yet
- **Virtual shell class paths:** 7 (items 24-30) -- noted but not added to registry map (Microsoft-only handlers)
- **Undocumented paths needing ProcMon validation:** Items 1-8, 11-12 need ProcMon traces. Item 13 (Services) should use SCM API rather than direct registry validation. Items 14-15 are read-only display.

---

### 3.3 Consistency Notes

1. **Autoruns "Explorer" count discrepancy:** The Autoruns analysis reports 147 Explorer entries, but the original Autoruns research plan referenced 166. The actual count after deduplication and filtering is 147. This is consistent -- the difference is due to duplicate entries from multi-profile scanning.

2. **ShellExView vs HKLM paths in Autoruns:** ShellExView enumerates `HKCR` (the merged view), while Autoruns references `HKLM\Software\Classes` (the machine hive specifically). These are equivalent for machine-wide registrations because `HKCR` merges `HKLM\Software\Classes` and `HKCU\Software\Classes`. No data discrepancy results from this difference.

3. **Disable mechanism consistency:** Both analyses confirm the dash-prefix approach for context menu handlers. The Autoruns analysis additionally documents the `AutorunsDisabled` subfolder approach for startup folder shortcuts and the `StartupApproved` binary blob approach used by Task Manager. These are complementary, not conflicting.

4. **Service registry path vs API:** Both the Autoruns analysis and the epic2 research reference `HKLM\System\CurrentControlSet\Services`, but the Autoruns analysis correctly notes that SCM API is the recommended approach over direct registry access. This is consistent and the registry map entries will note the API preference.
