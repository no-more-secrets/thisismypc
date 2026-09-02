# Startup Scanner Rationale

Design rationale for `ThisIsMyPC.Modules.Startup`. This is AI-written, condensed from an analysis of an Autoruns export from one personal machine (the export is not in the repo) and from Sam's read of how Autoruns stores state. Since 2026-09-02 the module's page is laid out like Autoruns itself: one tab per category plus Everything, a filter box and a "Hide Microsoft entries" box shared by every tab, and items disabled the way Autoruns disables them, so the two tools read each other's state. The earlier Startup, Scheduled Tasks, and Services tabs were removed the same day; their scanners and change factories stay in the module because sets (Clean Boot), the monitoring section on Home, and the set inspector still apply changes through them.

## The page

`AutorunsScanner` reads the locations in `AutorunLocations` plus both Startup folders, every scheduled task, and the Services key. `AutorunToggler` applies the change; `AutorunChangeFactory` builds the descriptor (`ChangeValueType.Autorun_State`, Before/After "Enabled" or "Disabled", the item named by `AutorunTarget` as `kind|location|name` in SystemLocation, where location is always a fixed catalog key, a folder, or a task path and name is the remainder, so a value or subkey name may contain `|`). Every toggle goes through the pending-changes queue like any other change, and undo is the opposite move. A row that sits in `AutorunsDisabled` gets a `|parked` suffix on its setting id, so a parked twin never shares an identity with the live item.

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

Rules the toggler keeps:

- Idempotent: an item already at its destination is success.
- Never overwrites: when the source and the destination both exist (a twin parked earlier by Autoruns, then the installer recreated the live item), the move fails before touching anything and names the copy to remove. Registry values, keys, and Startup files all follow this rule.
- A service or driver that is already `Start` = 4 without an `AutorunsDisabled` value was disabled by something else; both directions refuse rather than record a change with no reverse.

What the scanner reads:

- Disabled items come from the same parking places, so an item Autoruns disabled shows as Disabled here and the reverse.
- Only string-typed values are items; a DWORD or binary value under a text-only location is skipped.
- Per-user service instances (`Type` with 0x80) are skipped; the template is the item. Services and drivers with a manual start are not listed, as in Autoruns, unless an `AutorunsDisabled` value marks them as parked.
- Image paths come from the command line, the CLSID's `InprocServer32` (64-bit or `WOW6432Node` class table), a bare DLL name resolved against System32 or SysWOW64, the Winsock `LibraryPath`, an Office ProgID's CLSID, or the service `ImagePath` (`svchost` hosts show their `Parameters\ServiceDll`). Publisher and description come from the file's version resource, with the CLSID or add-in friendly name taking precedence.
- Run-key and Startup-folder items that Task Manager switched off carry the note "Off in Task Manager"; the page does not rewrite `StartupApproved`.
- Shell handlers the Context Menus page switched off (a dash before the CLSID in `(Default)`, or the CLSID on `Shell Extensions\Blocked`) show as off with the note "Off in Context Menus" and a greyed switch. That page owns their state; two mechanisms on one key would fight.

Restart requirements follow the category: Explorer handlers ask for an Explorer restart; services, drivers, font drivers, Drivers32, Known DLLs, credential providers, Winsock, and print monitors ask for a reboot.

## Why the list is this long

Every category beyond Logon and Scheduled Tasks is security infrastructure, boot-critical, or almost entirely Microsoft-owned: drivers can make the system unbootable, credential providers can lock every user out, Known DLLs hardens against search-order hijack, Winsock is the network stack. They are listed because Sam wanted the complete inventory with Autoruns-compatible toggles; the "Hide Microsoft entries" box is the same escape hatch Autoruns offers. Categories Autoruns has that this page does not: Boot Execute (`autocheck autochk *`), LSA providers, network providers, Codecs beyond `Drivers32`, Group Policy and PLAP Winlogon extensions, IE toolbars and URL search hooks, Hijacks. They are either boot-time plumbing or malware checks rather than settings.
