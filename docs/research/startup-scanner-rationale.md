# Startup Scanner Rationale

Design rationale for the scope of `ThisIsMyPC.Modules.Startup`. This is AI-written, condensed from an analysis of an Autoruns export from one personal machine (the export is not in the repo) and from Sam's read of how Autoruns stores state. The module has two views of the same machine: the curated Startup, Scheduled Tasks, and Services tabs, and since 2026-09-02 an Autoruns tab that lists every autostart location the way Sysinternals Autoruns does and disables items the way Autoruns does, so the two tools read each other's state.

## The curated tabs

| Tab | Scanner | Source | Toggle mechanism |
|---|---|---|---|
| Startup | `StartupScanner` | `Run` keys under HKLM, HKLM `WOW6432Node`, and HKCU; the user and common Startup folders through `IStartupFolderService`; enabled state from `Explorer\StartupApproved\{Run,Run32,StartupFolder}` | `StartupApproved` 12-byte blob (even first byte enabled, odd disabled), the same mechanism Task Manager and Settings use; the Run value or `.lnk` file never moves |
| Services | `ServiceScanner` | Service Control Manager through `IServiceControlService` | Start type through the SCM |
| Scheduled Tasks | `ScheduledTaskScanner` | Task Scheduler | Enabled flag through the scheduler |

## The Autoruns tab

`AutorunsScanner` reads the locations in `AutorunLocations` plus both Startup folders, every scheduled task, and the Services key. `AutorunToggler` applies the change; `AutorunChangeFactory` builds the descriptor (`ChangeValueType.Autorun_State`, Before/After "Enabled" or "Disabled", the item named by `AutorunTarget` as `kind|location|name` in SystemLocation). Every toggle goes through the pending-changes queue like any other change, and undo is the opposite move.

| Autoruns tab | Locations | Item kind | Disable = |
|---|---|---|---|
| Logon | `HKCU` and `HKLM` `...\CurrentVersion\Run`, HKLM `WOW6432Node` Run, `Active Setup\Installed Components` (subkeys with a `StubPath`), `SafeBoot\AlternateShell`, the user and common Startup folders | values, keys, files | move the value or key under an `AutorunsDisabled` subkey; move the file into an `AutorunsDisabled` subfolder |
| Explorer | `Classes\Directory\Background\ShellEx\ContextMenuHandlers`, `Explorer\ShellIconOverlayIdentifiers` | keys | move under `AutorunsDisabled` |
| Internet Explorer | `Explorer\Browser Helper Objects` (64-bit and `WOW6432Node`) | keys | move under `AutorunsDisabled` |
| Scheduled Tasks | the whole task library | tasks | scheduler Enabled flag |
| Services | `HKLM\SYSTEM\CurrentControlSet\Services` keys with a Win32 `Type` (0x10, 0x20) and `Start` 0, 1, or 2 | service keys | `Start` = 4, the old `Start` kept in an `AutorunsDisabled` DWORD |
| Drivers | same key, `Type` 1, 2, or 8, `Start` 0, 1, or 2 | service keys | same as Services |
| Font Drivers | `Windows NT\CurrentVersion\Font Drivers` | values | move under `AutorunsDisabled` |
| 32-Bit Drivers | `Windows NT\CurrentVersion\Drivers32` (64-bit and `WOW6432Node`) | values | move under `AutorunsDisabled` |
| Known DLLs | `Session Manager\KnownDlls` (the `DllDirectory` values are paths, not items) | values | move under `AutorunsDisabled` |
| Winlogon | `Authentication\Credential Providers` | keys | move under `AutorunsDisabled` |
| Winsock Providers | `WinSock2\Parameters\NameSpace_Catalog5\Catalog_Entries` and `Catalog_Entries64` (`LibraryPath`, `DisplayString`) | keys | move under `AutorunsDisabled` |
| Print Monitors | `Control\Print\Monitors` (`Driver`) | keys | move under `AutorunsDisabled` |
| Office | `Microsoft\Office\{Outlook,Excel,PowerPoint,Word}\Addins` (64-bit and `WOW6432Node`; `FriendlyName`) | keys | move under `AutorunsDisabled` |

Disabled items are read back from the same places, so an item Autoruns disabled shows as Disabled here and the reverse. Per-user service instances (`Type` with 0x80) are skipped; the template is the item. Services and drivers with a manual start are not listed, as in Autoruns, unless an `AutorunsDisabled` value marks them as parked. Image paths come from the command line, the CLSID's `InprocServer32` (64-bit or `WOW6432Node` class table), a bare DLL name resolved against System32 or SysWOW64, the Winsock `LibraryPath`, an Office ProgID's CLSID, or the service `ImagePath` (`svchost` hosts show their `Parameters\ServiceDll`). Publisher and description come from the file's version resource, with the CLSID or add-in friendly name taking precedence.

Run-key and Startup-folder items that Task Manager switched off carry the note "Off in Task Manager"; the Autoruns tab does not rewrite `StartupApproved`, and the Startup tab does not read `AutorunsDisabled`. An item moved by the Autoruns tab leaves the Startup tab until it is moved back. Restart requirements follow the category: Explorer handlers ask for an Explorer restart; services, drivers, font drivers, Drivers32, Known DLLs, credential providers, Winsock, and print monitors ask for a reboot.

## Why the curated tabs stay curated

Every category that only the Autoruns tab shows is security infrastructure, boot-critical, or almost entirely Microsoft-owned: drivers can make the system unbootable, credential providers can lock every user out, Known DLLs hardens against search-order hijack, Winsock is the network stack. The Autoruns tab lists them because Sam wanted the complete inventory with Autoruns-compatible toggles; the "Hide Microsoft entries" box is the same escape hatch Autoruns offers. Categories Autoruns has that neither view lists: Boot Execute (`autocheck autochk *`), LSA providers, network providers, Codecs beyond `Drivers32`, Group Policy and PLAP Winlogon extensions, IE toolbars and URL search hooks, Hijacks. They are either boot-time plumbing or malware checks rather than settings.
