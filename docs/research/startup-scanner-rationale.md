# Startup Scanner Rationale

Design rationale for the scope of `ThisIsMyPC.Modules.Startup`. This is AI-written research, condensed from an analysis of an Autoruns export. The export came from one personal machine and is not in the repo; only the conclusions that shaped the module are kept here. The module manages three Autoruns categories: Logon (startup entries), Services, and Scheduled Tasks. This document records why the other categories are left alone, and one compatibility note about startup folder shortcuts.

## Categories the module manages

| Autoruns category | Module scanner | Source |
|---|---|---|
| Logon | `StartupScanner` | `Run` keys under HKLM, HKLM `WOW6432Node`, and HKCU; the user and common Startup folders through `IStartupFolderService`; enabled state from `Explorer\StartupApproved\{Run,Run32,StartupFolder}` |
| Services | `ServiceScanner` | Service Control Manager through `IServiceControlService` |
| Scheduled Tasks | `ScheduledTaskScanner` | Task Scheduler |

## Categories left out, and why

| Category | Where it lives | Why not managed |
|---|---|---|
| Drivers | `HKLM\SYSTEM\CurrentControlSet\Services`, filtered by `Type` | Kernel-level. Disabling the wrong driver can make the system unbootable. Nearly all entries are enabled and most are Microsoft. |
| Codecs | DirectShow filter CLSIDs, `Drivers32` | Almost all Microsoft built-in. No user-facing reason to toggle. |
| Winlogon | Group Policy extensions, credential providers, PLAP providers | Security-sensitive. Disabling a credential provider can lock every user out. |
| Known DLLs | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDlls` | A hardening mechanism against DLL search-order hijack. Editing it weakens the system. |
| Network Providers | WinSock2 catalogs, `NetworkProvider` order | Network stack configuration, all Microsoft. |
| Internet Explorer | Browser helper objects, toolbars, URL search hooks | IE is retired on Windows 11. |
| Office Add-ins | `HKLM\SOFTWARE\Microsoft\Office\<App>\Addins` | Better managed inside the Office apps. |
| Print Monitors | `HKLM\SYSTEM\CurrentControlSet\Control\Print\Monitors` and `Providers` | Print subsystem; no user demand. |
| Boot Execute | `Session Manager\BootExecute` | Holds `autocheck autochk *`, a boot-time disk check the system depends on. |
| LSA Providers | `SecurityProviders`, `Authentication Packages`, `Notification Packages` | Authentication infrastructure. |
| Hijacks | Shell open commands for core file types | A malware check, not a setting. |

The shared theme: every excluded category is security infrastructure, boot-critical, or almost entirely Microsoft-owned with nothing a user would want to turn off. The Autoruns Explorer category (shell extensions) belongs to the context menu scanner in `ThisIsMyPC.Modules.Shell`, not to this module; see `context-menu-scanner-rationale.md`.

## Startup folder shortcuts: the Autoruns convention

Autoruns disables a Startup folder item by moving the `.lnk` file into an `AutorunsDisabled` subfolder:

- User: `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\AutorunsDisabled\`
- Machine: `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Startup\AutorunsDisabled\`

Windows records the enabled state of startup entries, including Startup folder items, in `StartupApproved` as a 12-byte `REG_BINARY` blob: even first byte enabled, odd first byte disabled, bytes 4 to 11 an optional disable-time `FILETIME`. `StartupScanner` and `StartupChangeFactory` use `StartupApproved`, the same mechanism Task Manager and Settings use, so a toggle made here shows correctly in those tools and the shortcut file never moves. Items a user disabled earlier with Autoruns sit in `AutorunsDisabled` and have no `StartupApproved` entry; the scanner does not read that subfolder. Reading it is a candidate improvement, not shipped.
