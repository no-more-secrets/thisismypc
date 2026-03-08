# Project Documentation Index

## Development Guides

| Document | Purpose |
|---|---|
| [avalonia-guide.md](avalonia-guide.md) | Avalonia UI patterns and constraints: compiled bindings, Classes binding, Popup constraints, command binding in DataTemplates, thread marshaling, NativeAOT constraints, CommunityToolkit.Mvvm conventions, font/theme patterns, testing patterns. Must-read before any story involving UI or ViewModels. |

## Windows Research

| Document | Purpose |
|---|---|
| [windows-settings-registry-map.md](windows-settings-registry-map.md) | Master registry reference mapping every Windows setting to its registry key. ProcMon-validated entries with schema, gotchas, and restart requirements. Source of truth for all registry-based implementations. |
| [windows-signal-flow.md](windows-signal-flow.md) | OS boot-to-desktop signal flow, registry as control plane, Explorer refresh vs restart, WM_SETTINGCHANGE, service control manager signals. |
| [research/epic2-registry-research.md](research/epic2-registry-research.md) | Epic 2 implementation-specific registry research: IRegistryService patterns, context menu handler enumeration, test sandbox key strategy, and ProcMon validation results. |
| [research/Shell_Extensions_List.html](research/Shell_Extensions_List.html) | ShellExView export from Windows 11 25H2 install. Ground truth for context menu handler enumeration (story 2.2). Lists all registered shell extensions with CLSIDs, DLL paths, and handler types. |
| [research/autoruns.csv](research/autoruns.csv) | Sysinternals Autoruns export from 25H2 install. Ground truth for startup entries, boot execute, scheduled tasks, services, and shell extensions (Epic 3: Startup & Services). Large file — load selectively by Category column. |

## Project Management

| Document | Purpose |
|---|---|
| [project-state.md](project-state.md) | Current project state and progress tracking. |
| [agentic-tooling.md](agentic-tooling.md) | Agentic workflow tooling and configuration for this project. |
