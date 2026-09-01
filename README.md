# ThisIsMyPC

A Windows 11 system-control app that puts the scattered power-user utilities
in one place and makes every change reversible. It covers the ground of
Autoruns, ShellExView, O&O ShutUp10, winutil, and UniGetUI, with a state capture for every change and thorough undo options. It is GPLv2, it has no
telemetry and no account, and it covers an entire Windows install.

This repository is developed by an AI coding agent (Claude Code) under a human
owner. This file and everything under `docs/` are AI-written and checked
against the code. If you are reading this through your own agent, the
operating rules it needs are in [CLAUDE.md](CLAUDE.md); the doc index with
what each file is for is [docs/README.md](docs/README.md).

## What it does

Ten modules currently, each one is a page in the .exe app:

| Group | Module | What it manages |
|---|---|---|
| Core | Explorer | Taskbar, context menu style, Explorer preferences, shell settings |
| Core | Context Menus | Every registered right-click handler: COM extensions, static verbs, drag-drop handlers, modern packaged handlers. Shows source file, publisher, and classification; toggles across all registration surfaces |
| Core | Environment | System and user environment variables, with a PATH editor |
| Core | Power Plans | Discover, switch, and adjust power plans |
| Core | Startup & Services | Startup entries, Windows services, scheduled tasks |
| System | Windows Annoyances | Nag screens, suggestions, ads, and upsell prompts |
| System | Windows Update | Update installs, forced restarts, driver overwrites, feature upgrades |
| System | Privacy & Telemetry | Diagnostic data, error reporting, tracking, personalization |
| System | Software | Install and uninstall apps from a curated catalog through winget; remove inbox apps; manage winget upgrades |
| Hardware | Display | Brightness, contrast, and input source over DDC/CI |

Module definitions: `src/ThisIsMyPC.Modules.*`, registered in
`src/ThisIsMyPC.App/App.axaml.cs`.

Tweak sets bundle related changes into one apply: Clean Boot, Nuke Copilot,
Privacy Baseline, Windows 10-ify, Windows Update Control. A set stages its
changes like any other edit; nothing is written until the user reviews and
applies. Sets are JSON files in `src/ThisIsMyPC.App/sets/`; the format is in
[docs/sets-schema.md](docs/sets-schema.md).

## How changes work

- Every edit is staged in a pending-changes list. The user sees the registry
  key, service, or task that will be touched and can deselect any item.
- Every change carries its before-state. `ChangeDescriptor` rejects a null
  `BeforeValue`, so a write without one cannot be staged.
- Apply runs as a group with rollback: if one change fails, the ones already
  applied are reverted.
- Change history persists in a SQLite database with undo.
- Installs and uninstalls are one-way, so they run through a separate actions
  queue (`IPendingActionsService`) with no fabricated before-state and no
  history entry.
- A system restore point is created before applying a batch of five or more
  changes, and the user can create one at any time from the review panel.

## Owner Mode

Windows reverts some settings on its own: feature updates, scheduled tasks,
and policy caches undo registry edits. Owner Mode is an optional background
service (`src/ThisIsMyPC.Service`, runs in Session 0 as SYSTEM) that re-applies
the settings the user chose and reports drift on the Home page when something
changed behind their back. It is off until turned on in Settings. The app and
the service talk over a hardened named pipe whose message envelope is in
`src/ThisIsMyPC.Ipc.Contracts`.

## Defaults

- No startup entry, no tray icon, no notifications, no background monitoring
  unless enabled in Settings.
- Network use: the update check against GitHub Releases (on by default, one
  toggle to turn off) and winget when the user installs software. Nothing else.
- All data lives in `%ProgramData%\ThisIsMyPC` with a DACL that only
  Administrators and SYSTEM can write. One database per machine, not per user.
- Updates are verified against a GPG-signed manifest before they are applied
  (`GpgManifestUpdateVerifier`; process in
  [docs/release/update-signing.md](docs/release/update-signing.md)).

## Requirements

- Windows 11, x64. The manifest also declares Windows 10 compatibility, but
  nothing has been tested there.
- Administrator rights. The app elevates at launch (`requireAdministrator` in
  `app.manifest`); it does nothing useful without elevation.
- Installed per machine from an MSI into `Program Files`. No per-user install,
  no portable build.

## Build and test

```
dotnet build --configuration Release
dotnet test --filter "Category!=Integration&Category!=Diagnostic"   # what CI runs
```

Tests tagged Integration or Diagnostic read the live system and are excluded
from CI. UI changes are verified with a headless Avalonia harness in
`tests/ThisIsMyPC.App.UiTests` that drives the real windows and writes
screenshots to `artifacts/ui-shots/`; an agent reads those PNGs instead of
asking a human to launch the app. Release builds are packed by
`tools/build-release.ps1` and can be published NativeAOT
([docs/release/packaging.md](docs/release/packaging.md)).

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
analyzers/                   Roslyn analyzer (TIPC001) that blocks unsafe DLL search paths
tests/                       One test project per module plus security, IPC, integration, UI
```

A module implements `IModule` (`src/ThisIsMyPC.Core/Modules/IModule.cs`:
info, availability, scan, apply, revert) and is registered explicitly with
`AddSingleton<IModule, X>()`. No reflection scanning, because NativeAOT.
Modules do not reference each other; shared system access goes through the
interop projects. A module that cannot run on the current machine shows up
disabled with the reason, not hidden. Reference implementations:
`Modules.Shell` for a custom view, `Modules.Annoyances` for the card renderer.

## Status

Feature work for the first release is complete. Remaining before the first
public release: release-key ceremony, code-signing certificate, and store
distribution. Current list:
[docs/planning/refinement-backlog.md](docs/planning/refinement-backlog.md).

Not built, tracked for later: OEM hardware modules (ASUS platform tuning,
RGB, fan control, drivers), network and firewall, exportable PC profiles,
OneDrive and Edge removal, hardware sensors.

## Contributing

Contributions are expected to arrive through agents as much as through
people. Point the agent at [CLAUDE.md](CLAUDE.md) first; it holds the build
commands, the test categories, the UI verification loop, and the architecture
rules that reviews enforce. The short version:

- Modules are the extension point. Implement `IModule`, register it, add a
  test project mirroring `src/`.
- Every reversible change goes through the pending-changes pipeline with a
  real before-state. That rule is what makes undo trustworthy.
- One-way operations go through the actions queue, never the change pipeline.
- Never shell out to opaque third-party binaries; port their registry or API
  recipes into set definitions or modules instead.
- No em dashes anywhere in the repo, including commit messages.

## License

[GPLv2](LICENSE), every component, including the Owner Mode service. Open
source makes the code inspectable; it is not a substitute for an audit. See
[docs/why-gplv2.md](docs/why-gplv2.md).

Published by No More Secrets, LLC.
