# Windows Settings → Registry Map

**ThisIsMyPC Master Reference**
**Started March 2026 — Living Document**
**Target: Windows 11 25H2**

---

## Purpose

This document maps every Windows setting to its underlying registry key(s). It serves as the single source of truth for ThisIsMyPC's implementation — if a setting isn't validated here, it doesn't get implemented.

---

## Validation Standards

### How to Add an Entry

Every entry in this document must be ProcMon-validated before it's considered implementation-ready. No exceptions for "I found it on a blog" or "it worked on 24H2."

**Required steps:**

1. Open ProcMon. Set filter: **Path** → **contains** → `<target key area>` → **Include**.
2. Toggle the setting via the Windows UI (Settings app, Control Panel, or wherever it lives).
3. Record exactly what ProcMon shows: process name, operation, full path, value name, type, data before, data after, result.
4. Toggle the setting back. Confirm the reverse write.
5. Test the restart requirement: does the change take effect immediately? After Explorer refresh (F5)? After Explorer restart? After sign-out? After reboot?
6. Add the entry to this document following the schema below.

### Entry Schema

Every entry must include all of the following fields:

| Field | Description |
|---|---|
| **UI Location** | Exact click path to the setting (e.g., `Settings → Personalization → Taskbar → Taskbar behaviors → Taskbar alignment`) |
| **Key** | Full registry path |
| **Value** | Value name |
| **Type** | REG_DWORD, REG_SZ, REG_EXPAND_SZ, etc. |
| **Data** | All valid values and what they mean |
| **Default** | Factory default on a clean install |
| **Scope** | HKCU (per-user) or HKLM (machine-wide, requires admin) |
| **Effect** | Immediate / Explorer refresh / Explorer restart / Sign-out / Reboot |
| **Writer** | Process that performs the write (from ProcMon) |
| **Watchers** | Processes that read the value after write (from ProcMon), if observed |
| **Validated** | Date and Windows build (e.g., `2026-03-07, 25H2 build 26xxx`) |
| **Gotchas** | Any quirks, protections (UCPD), or known regressions |

### Entry Status Tags

Each entry gets one of these tags in its heading:

- `[CONFIRMED]` — ProcMon-validated on our target build
- `[RESEARCHED]` — From reliable sources but not yet ProcMon-validated
- `[STALE]` — Was confirmed on a previous build, needs revalidation
- `[BLOCKED]` — Write-protected by UCPD or other system mechanism

### Naming Conventions

- Use the exact value name from the registry, case-sensitive (e.g., `TaskbarAl` not `taskbaral`)
- Use full registry paths with no abbreviations (e.g., `HKCU\Software\Microsoft\...` not `HKCU\...\Advanced`)
- Group entries by the ThisIsMyPC module/story they belong to, then by UI location

### When to Revalidate

- After every Windows feature update (e.g., 25H2 → 26H2)
- If a user reports a setting not working
- If Microsoft announces changes to Settings app behavior
- Mark revalidated entries with the new date; mark outdated ones as `[STALE]`

---

## Shell & Explorer (Epic 2)

### Taskbar

#### Taskbar Alignment `[CONFIRMED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → Personalization → Taskbar → Taskbar behaviors → Taskbar alignment` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value** | `TaskbarAl` |
| **Type** | REG_DWORD |
| **Data** | `0` = Left, `1` = Center |
| **Default** | `1` (Center) |
| **Scope** | HKCU |
| **Effect** | Immediate |
| **Writer** | SystemSettings.exe |
| **Watchers** | Explorer.EXE, StartMenuExperienceHost.exe |
| **Validated** | 2026-03-07, 25H2 |
| **Gotchas** | Value name ends in lowercase `l` (ell), easy to misread as capital `I`. No UCPD interference observed. |

#### Widgets Toggle `[CONFIRMED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → Personalization → Taskbar → Taskbar items → Widgets` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value** | `TaskbarDa` |
| **Type** | REG_DWORD |
| **Data** | `0` = Hidden, `1` = Shown |
| **Default** | `1` (Shown) |
| **Scope** | HKCU |
| **Effect** | Immediate |
| **Writer** | SystemSettings.exe |
| **Watchers** | Explorer.EXE (immediate read after write) |
| **Validated** | 2026-03-07, 25H2 |
| **Gotchas** | UCPD write-protection reported on some 24H2 builds; NOT observed on our 25H2 install. Still catch ACCESS_DENIED gracefully. |

#### Widgets Full Disable (Policy) `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `gpedit.msc → Computer Configuration → Administrative Templates → Windows Components → Widgets → Allow Widgets` |
| **Key** | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` |
| **Value** | `AllowNewsAndInterests` |
| **Type** | REG_DWORD |
| **Data** | `0` = Fully disabled (including Win+W), `1` = Enabled |
| **Default** | Value absent (Widgets enabled) |
| **Scope** | HKLM (requires admin) |
| **Effect** | Reboot or `gpupdate /force` |
| **Writer** | Group Policy engine |
| **Watchers** | — |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | This is the nuclear option — disables the entire Widgets feature, not just the taskbar icon. Different from TaskbarDa which only hides the button. |

### Context Menu

#### Classic Context Menu (Win10-style) `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | None — no built-in Settings toggle |
| **Key** | `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` |
| **Value** | `(Default)` |
| **Type** | REG_SZ |
| **Data** | `""` (empty string) = Classic menu enabled. Key absent = Win11 menu. |
| **Default** | Key does not exist (Win11 modern menu) |
| **Scope** | HKCU |
| **Effect** | Explorer restart |
| **Writer** | Manual / ThisIsMyPC |
| **Watchers** | Explorer.EXE (on restart) |
| **Validated** | Not yet ProcMon-validated (no Settings toggle to trace; widely documented, stable since Win11 RTM) |
| **Gotchas** | The `(Default)` value must be empty string, NOT "value not set." Reversal = delete the entire `{86ca1aa0-...}` key. The CLSID masks the Win11 modern context menu COM object. |

### Explorer Preferences

#### Show Hidden Files `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `File Explorer → View → Show → Hidden items` (Win11) or `Folder Options → View → Show hidden files` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value** | `Hidden` |
| **Type** | REG_DWORD |
| **Data** | `1` = Show hidden files, `2` = Don't show |
| **Default** | `2` |
| **Scope** | HKCU |
| **Effect** | Explorer refresh (F5) |
| **Writer** | Explorer.EXE |
| **Watchers** | Explorer.EXE |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Value is `1`/`2`, not `0`/`1` — easy mistake |

#### Show File Extensions `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `File Explorer → View → Show → File name extensions` (Win11) or `Folder Options → View → Hide extensions for known file types` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value** | `HideFileExt` |
| **Type** | REG_DWORD |
| **Data** | `0` = Show extensions, `1` = Hide extensions |
| **Default** | `1` (hidden) |
| **Scope** | HKCU |
| **Effect** | Explorer refresh (F5) |
| **Writer** | Explorer.EXE |
| **Watchers** | Explorer.EXE |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Inverted logic — `0` means show, `1` means hide |

#### Show Protected OS Files `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Folder Options → View → Hide protected operating system files` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value** | `ShowSuperHidden` |
| **Type** | REG_DWORD |
| **Data** | `1` = Show, `0` = Hide |
| **Default** | `0` (hidden) |
| **Scope** | HKCU |
| **Effect** | Explorer refresh (F5) |
| **Writer** | Explorer.EXE |
| **Watchers** | Explorer.EXE |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | There's also a `SuperHidden` value in the same key — it's inert (a legacy typo). Only `ShowSuperHidden` does anything. |

#### Launch Explorer To `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Folder Options → General → Open File Explorer to` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer` |
| **Value** | `LaunchTo` |
| **Type** | REG_DWORD |
| **Data** | `1` = This PC, `2` = Quick Access, `3` = Home (Win11 22H2+) |
| **Default** | `2` or `3` depending on build |
| **Scope** | HKCU |
| **Effect** | Next Explorer window opened |
| **Writer** | Explorer.EXE |
| **Watchers** | Explorer.EXE |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Value `3` (Home) was added in 22H2. Older docs only list 1 and 2. |

#### Separate Process for Folders `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Folder Options → View → Launch folder windows in a separate process` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value** | `SeparateProcess` |
| **Type** | REG_DWORD |
| **Data** | `1` = Yes, `0` = No |
| **Default** | `0` |
| **Scope** | HKCU |
| **Effect** | Explorer restart |
| **Writer** | Explorer.EXE |
| **Watchers** | Explorer.EXE |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | None known |

#### Sync Provider Notifications (Explorer Ads) `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Folder Options → View → Show sync provider notifications` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value** | `ShowSyncProviderNotifications` |
| **Type** | REG_DWORD |
| **Data** | `0` = Off (no ads), `1` = On |
| **Default** | `1` |
| **Scope** | HKCU |
| **Effect** | Explorer refresh |
| **Writer** | Explorer.EXE |
| **Watchers** | Explorer.EXE |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | This is the "OneDrive ads in Explorer" setting |

### Notification Suppression (ContentDeliveryManager)

All under `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager`. All REG_DWORD. All `0` = disabled, `1` = enabled (default).

#### Tips and Suggestions `[CONFIRMED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → System → Notifications → Additional settings → Get tips and suggestions when using Windows` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` |
| **Value** | `SubscribedContent-338389Enabled` |
| **Type** | REG_DWORD |
| **Data** | `0` = Disabled, `1` = Enabled |
| **Default** | `1` |
| **Scope** | HKCU |
| **Effect** | Next time tips would be shown |
| **Writer** | SystemSettings.exe |
| **Watchers** | Explorer.EXE (reads `SoftLandingEnabled` after this write) |
| **Validated** | 2026-03-07, 25H2 |
| **Gotchas** | Explorer reads `SoftLandingEnabled` as a companion value — both should be set to 0 for complete suppression |

#### Suggested Content in Settings `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → Privacy & security → General → Show me suggested content in the Settings app` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` |
| **Value** | `SubscribedContent-338393Enabled` |
| **Type** | REG_DWORD |
| **Data** | `0` = Disabled, `1` = Enabled |
| **Default** | `1` |
| **Scope** | HKCU |
| **Effect** | Next Settings app launch |
| **Writer** | SystemSettings.exe |
| **Watchers** | — |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Two additional values also control Settings suggestions: `SubscribedContent-353694Enabled` and `SubscribedContent-353696Enabled`. Set all three to 0. |

#### Finish Setup / Welcome Experience `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → System → Notifications → Additional settings → Suggest ways to get the most out of Windows...` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` |
| **Value** | `SubscribedContent-310093Enabled` |
| **Type** | REG_DWORD |
| **Data** | `0` = Disabled, `1` = Enabled |
| **Default** | `1` |
| **Scope** | HKCU |
| **Effect** | Next logon |
| **Writer** | SystemSettings.exe |
| **Watchers** | — |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Also set `ScoobeSystemSettingEnabled` to `0` at `HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement` for complete suppression |

#### Lock Screen Ads / Spotlight Tips `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → Personalization → Lock screen → Get fun facts, tips, tricks, and more on your lock screen` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` |
| **Value** | `SubscribedContent-338387Enabled` |
| **Type** | REG_DWORD |
| **Data** | `0` = Disabled, `1` = Enabled |
| **Default** | `1` |
| **Scope** | HKCU |
| **Effect** | Next lock screen display |
| **Writer** | SystemSettings.exe |
| **Watchers** | — |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Also set `RotatingLockScreenOverlayEnabled` to `0` in the same key. For full Spotlight disable, also set `RotatingLockScreenEnabled` to `0`. |

#### Silent App Installs `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | No direct toggle — controlled by Store/content delivery |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` |
| **Value** | `SilentInstalledAppsEnabled` |
| **Type** | REG_DWORD |
| **Data** | `0` = Disabled, `1` = Enabled |
| **Default** | `1` |
| **Scope** | HKCU |
| **Effect** | Next content delivery cycle |
| **Writer** | ContentDeliveryManager service |
| **Watchers** | — |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | This prevents Windows from auto-installing apps like Candy Crush, TikTok, etc. Feature updates may reset this to 1. |

#### SoftLandingEnabled (Tips Balloon) `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | No direct toggle — companion to SubscribedContent-338389Enabled |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` |
| **Value** | `SoftLandingEnabled` |
| **Type** | REG_DWORD |
| **Data** | `0` = Disabled, `1` = Enabled |
| **Default** | `1` |
| **Scope** | HKCU |
| **Effect** | Immediate (Explorer reads this live) |
| **Writer** | — |
| **Watchers** | Explorer.EXE (confirmed via ProcMon — reads this after SubscribedContent-338389Enabled changes) |
| **Validated** | 2026-03-07, 25H2 (observed as watcher, not directly toggled) |
| **Gotchas** | No Settings UI toggle for this — it's a companion value. Set it alongside SubscribedContent-338389Enabled for complete suppression. |

#### Confirm File Delete Dialog `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Recycle Bin → Properties → Display delete confirmation dialog` |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer` |
| **Value** | `ConfirmFileDelete` |
| **Type** | REG_DWORD |
| **Data** | `1` = Show prompt, `0` = Skip prompt |
| **Default** | Depends on Recycle Bin properties (typically `0` on clean install) |
| **Scope** | HKCU |
| **Effect** | Immediate |
| **Writer** | Explorer.EXE |
| **Watchers** | Explorer.EXE |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | This is a policy-level override. The primary control is the Recycle Bin properties dialog, which writes this value. The `Policies\Explorer` key may not exist until the user changes this setting. |

### Environment Variables (Story 2.5)

#### User Environment Variables `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → System → About → Advanced system settings → Environment Variables → User variables` |
| **Key** | `HKCU\Environment` |
| **Value** | (variable name, e.g., `PATH`, `TEMP`) |
| **Type** | REG_SZ or REG_EXPAND_SZ |
| **Data** | Variable value |
| **Default** | Varies |
| **Scope** | HKCU |
| **Effect** | After `WM_SETTINGCHANGE` broadcast — new processes only |
| **Writer** | SystemPropertiesAdvanced.exe / rundll32.exe |
| **Watchers** | All processes (via WM_SETTINGCHANGE) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Must broadcast `WM_SETTINGCHANGE` with lParam `"Environment"` after write or changes are invisible to running processes |

#### System Environment Variables `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → System → About → Advanced system settings → Environment Variables → System variables` |
| **Key** | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` |
| **Value** | (variable name, e.g., `PATH`, `PROCESSOR_ARCHITECTURE`) |
| **Type** | REG_SZ or REG_EXPAND_SZ |
| **Data** | Variable value |
| **Default** | Varies |
| **Scope** | HKLM (requires admin) |
| **Effect** | After `WM_SETTINGCHANGE` broadcast — new processes only |
| **Writer** | SystemPropertiesAdvanced.exe / rundll32.exe |
| **Watchers** | All processes (via WM_SETTINGCHANGE) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Requires admin elevation. PATH has a ~2048 char practical limit. System PATH and User PATH are concatenated at logon. |

---

## Future Epics (Placeholder Sections)

### Startup & Services (Epic 3)

#### Startup — User Run Key `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Settings → Apps → Startup` (partial), `Task Manager → Startup` |
| **Key** | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` |
| **Value** | (entry name, e.g., `Steam`, `Discord`, `OneDrive`) |
| **Type** | REG_SZ |
| **Data** | Full command line to the executable (e.g., `"C:\Program Files\Steam\steam.exe" -silent`) |
| **Default** | Varies per installed software |
| **Scope** | HKCU (per-user, no admin required) |
| **Effect** | Next logon |
| **Writer** | Application installers, user configuration |
| **Watchers** | Explorer.EXE (at logon), Task Manager |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | This is the most common startup location for user-installed apps. Autoruns export shows 13 entries on a power-user machine. Task Manager uses `StartupApproved\Run` (see below) to disable entries without deleting them. |

#### Startup — Machine Run Key `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Task Manager → Startup` (machine-wide entries also appear here) |
| **Key** | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` |
| **Value** | (entry name) |
| **Type** | REG_SZ |
| **Data** | Full command line |
| **Default** | Varies |
| **Scope** | HKLM (requires admin to modify) |
| **Effect** | Next logon (applies to all users) |
| **Writer** | Application installers |
| **Watchers** | Explorer.EXE (at logon) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Typically has fewer entries than HKCU Run. Autoruns export shows 1 entry here. |

#### Startup — Machine Run Key (32-bit) `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Task Manager → Startup` |
| **Key** | `HKLM\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run` |
| **Value** | (entry name) |
| **Type** | REG_SZ |
| **Data** | Full command line |
| **Default** | Varies |
| **Scope** | HKLM (requires admin to modify) |
| **Effect** | Next logon |
| **Writer** | 32-bit application installers |
| **Watchers** | Explorer.EXE (at logon) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | On 64-bit Windows, 32-bit installers often write here instead of the native Run key. Autoruns export shows 13 entries -- more than the native HKLM Run key. |

#### Startup — User Startup Folder `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `shell:startup` in Explorer address bar |
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Startup` |
| **Value** | `Startup` |
| **Type** | REG_SZ |
| **Data** | Path to user Startup folder (typically `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`) |
| **Default** | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup` |
| **Scope** | HKCU |
| **Effect** | Next logon |
| **Writer** | Windows shell |
| **Watchers** | Explorer.EXE (at logon) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | The registry value points to the folder path. The actual startup items are `.lnk` shortcuts in that folder. To disable, Autoruns moves shortcuts to an `AutorunsDisabled` subfolder. 4 entries in export. |

#### Startup — Common Startup Folder `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `shell:common startup` in Explorer address bar |
| **Key** | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Common Startup` |
| **Value** | `Common Startup` |
| **Type** | REG_SZ |
| **Data** | Path to all-users Startup folder (typically `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup`) |
| **Default** | `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup` |
| **Scope** | HKLM (requires admin to modify contents) |
| **Effect** | Next logon (all users) |
| **Writer** | Application installers |
| **Watchers** | Explorer.EXE (at logon) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Same `AutorunsDisabled` subfolder convention for disabling. 5 entries in export. |

#### Startup — StartupApproved (Task Manager Disable State) `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `Task Manager → Startup → right-click → Disable` |
| **Key** | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run` |
| **Value** | (matches the entry name in the corresponding Run key) |
| **Type** | REG_BINARY |
| **Data** | 12-byte blob: first byte `02` = enabled, `03` = disabled; remaining bytes are a FILETIME timestamp |
| **Default** | Value absent (entry enabled) or `02 00 00 ...` |
| **Scope** | HKCU |
| **Effect** | Next logon |
| **Writer** | Task Manager, Settings app |
| **Watchers** | Explorer.EXE (at logon) |
| **Validated** | Not yet ProcMon-validated -- confirm byte format on 25H2 |
| **Gotchas** | This is how Task Manager disables startup items without deleting the Run key entry. Critical for compatibility -- ThisIsMyPC must read and honor these values. Additional variants exist: `StartupApproved\Run32` (HKCU and HKLM), `StartupApproved\Run` (HKLM), `StartupApproved\StartupFolder` (HKLM). |

#### Startup — Active Setup `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | None — no Windows UI for Active Setup |
| **Key** | `HKLM\SOFTWARE\Microsoft\Active Setup\Installed Components\{GUID}` |
| **Value** | `StubPath` (REG_SZ — command to run), `IsInstalled` (REG_DWORD), `Version` (REG_SZ) |
| **Type** | Multiple values per subkey |
| **Data** | `StubPath` = command line; `IsInstalled` = `1` (active) or `0` (inactive); `Version` = version string |
| **Default** | Varies per component |
| **Scope** | HKLM (requires admin) |
| **Effect** | Next first-logon for a new user profile |
| **Writer** | Application installers, Windows Setup |
| **Watchers** | Explorer.EXE (at first logon per user) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Active Setup runs commands once per user at first logon. Each user profile tracks which components have run via `HKCU\Software\Microsoft\Active Setup\Installed Components`. 32-bit variant at `HKLM\SOFTWARE\Wow6432Node\Microsoft\Active Setup\Installed Components`. 8 + 2 entries in export. Display as read-only unless specifically targeting Active Setup management. |

#### Startup — Winlogon Shell `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | None — no Windows UI |
| **Key** | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` |
| **Value** | `Shell` |
| **Type** | REG_SZ |
| **Data** | `explorer.exe` (default Windows shell) |
| **Default** | `explorer.exe` |
| **Scope** | HKLM |
| **Effect** | Next logon |
| **Writer** | Windows Setup |
| **Watchers** | Winlogon.exe |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | Modifying this replaces the Windows shell entirely. Display as read-only / informational in the UI. Malware sometimes appends additional executables here. |

#### Startup — Winlogon Userinit `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | None — no Windows UI |
| **Key** | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon` |
| **Value** | `Userinit` |
| **Type** | REG_SZ |
| **Data** | `C:\Windows\system32\userinit.exe,` (note trailing comma) |
| **Default** | `C:\Windows\system32\userinit.exe,` |
| **Scope** | HKLM |
| **Effect** | Next logon |
| **Writer** | Windows Setup |
| **Watchers** | Winlogon.exe |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | System-critical. Modifying or deleting this prevents user logon. Display as read-only / informational. The trailing comma allows additional programs to be appended (comma-separated). |

#### Services — Service Registry Location `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `services.msc`, `Task Manager → Services` |
| **Key** | `HKLM\System\CurrentControlSet\Services\<ServiceName>` |
| **Value** | `Start` (REG_DWORD), `Type` (REG_DWORD), `ImagePath` (REG_EXPAND_SZ), `DisplayName` (REG_SZ), `Description` (REG_SZ), `ObjectName` (REG_SZ) |
| **Type** | Multiple values per subkey |
| **Data** | `Start`: `0`=Boot, `1`=System, `2`=Automatic, `3`=Manual, `4`=Disabled |
| **Default** | Varies per service |
| **Scope** | HKLM (requires admin) |
| **Effect** | Next service start or reboot (depending on start type) |
| **Writer** | Service Control Manager (SCM), application installers |
| **Watchers** | services.exe (SCM) |
| **Validated** | Not yet ProcMon-validated |
| **Gotchas** | **Prefer the SCM API** (`OpenSCManager`/`OpenService`/`ChangeServiceConfig`) over direct registry writes. Direct registry modification bypasses SCM validation and can cause inconsistencies. The registry path is documented here for read-only scanning and as the underlying storage reference. 327 entries in export (296 Microsoft, 29 third-party). |

#### Scheduled Tasks — Task Scheduler (API, Not Registry) `[RESEARCHED]`

| Field | Value |
|---|---|
| **UI Location** | `taskschd.msc`, `schtasks.exe` |
| **Key** | N/A — not stored in registry |
| **Value** | N/A |
| **Type** | XML files in `%SystemRoot%\System32\Tasks\<TaskPath>` |
| **Data** | Task definitions (triggers, actions, conditions, settings) |
| **Default** | Varies |
| **Scope** | Machine-wide (admin required for most operations) |
| **Effect** | Immediate (Task Scheduler service picks up changes) |
| **Writer** | Task Scheduler service, `schtasks.exe`, application installers |
| **Watchers** | Schedule service (svsvc) |
| **Validated** | N/A — API-based, no registry to validate |
| **Gotchas** | Use the Task Scheduler COM API (`ITaskService`, `ITaskFolder`, `IRegisteredTask`) or the `Microsoft.Win32.TaskScheduler` NuGet package. Do NOT parse XML files directly. 278 entries in export (249 Microsoft, 16 third-party). Many third-party tasks have empty Company fields — fall back to executable version info. |

### Power Plans (Epic 4)

_To be populated as settings are ProcMon-validated._

### Privacy & Telemetry Control (Epic 18)

_To be populated as settings are ProcMon-validated._

### Windows Update Control (Epic 19)

_To be populated as settings are ProcMon-validated._

### Network & Firewall Management (Epic 20)

_To be populated as settings are ProcMon-validated._

---

## Appendix: Context Menu Handler Enumeration Paths

These are not individual settings but scan targets for story 2.2. Context menu handlers are registered across multiple HKCR locations:

| Registry Path | Applies To |
|---|---|
| `HKCR\*\shellex\ContextMenuHandlers\` | All files |
| `HKCR\AllFilesystemObjects\shellex\ContextMenuHandlers\` | All files and file folders |
| `HKCR\Directory\shellex\ContextMenuHandlers\` | File folders |
| `HKCR\Directory\Background\shellex\ContextMenuHandlers\` | Desktop / folder background |
| `HKCR\Folder\shellex\ContextMenuHandlers\` | All folders (including virtual) |
| `HKCR\<ProgID>\shellex\ContextMenuHandlers\` | Per-file-type |

To disable a handler, prefix its `(Default)` CLSID value with `-` (e.g., `{GUID}` → `-{GUID}`). To resolve handler details, look up `HKCR\CLSID\{GUID}\InprocServer32\(Default)` for the DLL path.

---

## Appendix: ProcMon Quick Reference

For anyone on the team who needs to validate a new setting:

1. Download ProcMon from [Sysinternals](https://learn.microsoft.com/en-us/sysinternals/downloads/procmon)
2. Run as admin
3. Open Filter (Ctrl+L)
4. Set: **Path** → **contains** → `<key fragment>` → **Include** → **Add**
5. Click **Apply**, **OK**
6. Toggle the setting in Windows
7. Record the entries: process, operation, path, value, type, data, result
8. Add to this document following the schema above
