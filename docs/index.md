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
| [research/context-menu-handlers-analysis.md](research/context-menu-handlers-analysis.md) | Analyzed inventory of all 48 context menu handlers from ShellExView export. Classified as critical/system/optional/third-party with safe-to-disable guidance, CLSID sets, and cross-reference against documented enumeration paths. Primary reference for story 2.2 implementation. |
| [research/autoruns-analysis.md](research/autoruns-analysis.md) | Analyzed breakdown of 1846 Autoruns entries mapped to Epic 3 stories: startup registry paths (3.1/3.2), services via SCM API (3.3), scheduled tasks via COM API (3.4). Includes data model proposals, MS vs third-party splits, enabled/disabled distributions, and Phase 3 cross-reference audit. |
| [research/Shell_Extensions_List.html](research/Shell_Extensions_List.html) | ShellExView raw export from Windows 11 25H2 install. Ground truth for context menu handler enumeration (story 2.2). Lists all registered shell extensions with CLSIDs, DLL paths, and handler types. See context-menu-handlers-analysis.md for the processed version. |
| [research/autoruns.csv](research/autoruns.csv) | Sysinternals Autoruns raw export from 25H2 install. Ground truth for startup entries, boot execute, scheduled tasks, services, and shell extensions (Epic 3). Large file — load selectively by Category column. See autoruns-analysis.md for the processed version. |

## Project Management

| Document | Purpose |
|---|---|
| [project-state.md](project-state.md) | Current project state and progress tracking. |
| [agentic-tooling.md](agentic-tooling.md) | Agentic workflow tooling and configuration for this project. |
