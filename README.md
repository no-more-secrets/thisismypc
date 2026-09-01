# ThisIsMyPC

A Windows 11 system-control app that puts the scattered power-user utilities
in one place and makes every change reversible. It covers the ground of
Autoruns, ShellExView, O&O ShutUp10, winutil, and UniGetUI, with a before-state
captured for every mutation and undo for all of it. GPLv2, no telemetry, no
account, one install per machine.

## What it does

Ten modules, each one a page in the app:

| Group | Module | What it manages |
|---|---|---|
| Core | Explorer | Taskbar, context menu style, Explorer preferences, shell settings |
| Core | Context Menus | Every registered right-click handler: COM extensions, static verbs, drag-drop handlers, modern packaged handlers. Shows source file, publisher, and classification; toggles across all registration surfaces |
| Core | Environment | System and user environment variables, with a proper PATH editor |
| Core | Power Plans | Discover, switch, and adjust power plans |
| Core | Startup & Services | Startup entries, Windows services, scheduled tasks |
| System | Windows Annoyances | Nag screens, suggestions, ads, and upsell prompts |
| System | Windows Update | Update installs, forced restarts, driver overwrites, feature upgrades |
| System | Privacy & Telemetry | Diagnostic data, error reporting, tracking, personalization |
| System | Software | Install and uninstall apps from a curated catalog through winget; remove inbox apps; manage winget upgrades |
| Hardware | Display | Brightness, contrast, and input source over DDC/CI |

Tweak sets bundle related changes into one click: Clean Boot, Nuke Copilot,
Privacy Baseline, Windows 10-ify, Windows Update Control. A set stages its
changes like any other edit; nothing is written until you review and apply.
Sets are JSON files ([schema](docs/sets-schema.md)), so you can write your own.

## How changes work

- Every edit is staged in a pending-changes list. You see the registry key,
  service, or task that will be touched, and can deselect any item.
- Every change carries its before-state. The app refuses a write without one.
- Apply runs as a group with rollback: if one change fails, the ones already
  applied are reverted.
- Change history persists across sessions with undo, so you can revert
  something weeks later.
- Installs and uninstalls are one-way, so they run through a separate actions
  queue with no fabricated before-state and no history entry.
- A system restore point is created before applying a batch of five or more
  changes, and you can create one at any time from the review panel.

## Owner Mode

Windows reverts some settings on its own: feature updates, scheduled tasks,
and policy caches undo registry edits. Owner Mode is an optional background
service (Session 0, SYSTEM) that re-applies the settings you chose and reports
drift on the Home page when something changed behind your back. It is off
until you turn it on in Settings. The app and the service talk over a hardened
named pipe; both are in this repo.

## Defaults

- No startup entry, no tray icon, no notifications, no background monitoring
  unless you enable them in Settings.
- Network use: the update check against GitHub Releases (on by default, one
  toggle to turn off) and winget when you install software. Nothing else.
- All data lives in `%ProgramData%\ThisIsMyPC` with a DACL that only
  Administrators and SYSTEM can write. One database per machine, not per user.
- Updates are verified against a GPG-signed manifest before they are applied.

## Requirements

- Windows 11, x64.
- Administrator rights. The app elevates at launch; it is a system-control
  tool and does nothing useful without elevation.
- Installed per machine from an MSI into `Program Files`. No per-user install,
  no portable build.

## Build

```
dotnet build --configuration Release
dotnet test --filter "Category!=Integration&Category!=Diagnostic"   # what CI runs
```

Integration and Diagnostic tests read the live system and are excluded from
CI. UI changes are verified with a headless Avalonia harness
(`tests/ThisIsMyPC.App.UiTests`) that drives the real windows and saves
screenshots. Release builds can be published NativeAOT
(`tools/build-release.ps1`, see [packaging](docs/release/packaging.md)).

## Architecture

.NET 10, C# 14, Avalonia 11, CommunityToolkit.Mvvm. P/Invoke through CsWin32,
COM through hand-rolled vtables so the app stays NativeAOT-clean. SQLite for
history, Serilog for logs, Velopack for updates, xUnit for tests.

```
src/
  ThisIsMyPC.App             Avalonia host: navigation, pending changes UI, settings
  ThisIsMyPC.Core            Module contract, change pipeline, history, sets. No Win32 calls
  ThisIsMyPC.Interop.Win32   CsWin32 P/Invoke: SCM, power, DDC/CI, restore points
  ThisIsMyPC.Interop.Com     Task Scheduler, shell extension probing
  ThisIsMyPC.Interop.Wmi     WMI queries
  ThisIsMyPC.Modules.*       Shell, Startup, Power, Annoyances, WindowsUpdate, Privacy, Software, Display
  ThisIsMyPC.Service         Owner Mode service (Session 0)
  ThisIsMyPC.Ipc.Contracts   Named-pipe message envelope shared by app and service
analyzers/                   Roslyn analyzer that blocks unsafe DLL search paths
tests/                       One test project per module plus security, IPC, integration, UI
```

A module implements `IModule` (scan, apply, revert, availability) and is
registered explicitly in `App.axaml.cs`. No reflection scanning. Modules do
not reference each other; shared system access goes through the interop
projects. A module that cannot run on the current machine shows up disabled
with the reason, not hidden.

More in [docs/](docs/README.md).

## Status

Feature work for the first release is complete. Remaining before the first
public release: release-key ceremony, code-signing certificate, and store
distribution. See the [backlog](docs/planning/refinement-backlog.md).

Not built yet, tracked for later: OEM hardware modules (ASUS platform tuning,
RGB, fan control, drivers), network and firewall, exportable PC profiles,
OneDrive and Edge removal, hardware sensors.

## Contributing

Modules are the extension point. Pick a settings area or a hardware platform,
implement `IModule` following `Modules.Shell` (custom view) or
`Modules.Annoyances` (card renderer), register it, and add a test project.
Every reversible change must go through the pending-changes pipeline with a
real before-state; that rule is what makes undo trustworthy, and reviews
enforce it. Never shell out to opaque third-party binaries; port their
registry or API recipes into set definitions or modules instead.

## License

[GPLv2](LICENSE), every component, including the Owner Mode service. Open
source makes the code inspectable; it is not a substitute for an audit. See
[Why GPLv2](docs/why-gplv2.md).

Published by No More Secrets, LLC.
