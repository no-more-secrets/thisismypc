# Project Documentation Index

Operating rules live in the repo root `CLAUDE.md`. Plans and status live in
`planning/refinement-backlog.md`.

## Development

| Document | Purpose |
|---|---|
| [avalonia-guide.md](avalonia-guide.md) | Avalonia UI patterns and constraints: compiled bindings, Popup limits, command binding in DataTemplates, thread marshaling, NativeAOT constraints, CommunityToolkit.Mvvm conventions. |
| [nativeaot-com-interop-concerns.md](nativeaot-com-interop-concerns.md) | Why COM interop needs source-generated wrappers under NativeAOT, and which surfaces are affected. |
| [sets-schema.md](sets-schema.md) | JSON schema for tweak set definitions (built-in and user sets). |
| [diagnostic-tests.md](diagnostic-tests.md) | Live-system diagnostic tests that dump what the app would show, without launching the GUI. |
| [testing/context-menu-diagnostics.md](testing/context-menu-diagnostics.md) | Running the context menu diagnostic and integration tests. |
| [agentic-tooling.md](agentic-tooling.md) | Claude Code skills, MCP servers, and IDE setup used on this codebase. |

## Planning

| Document | Purpose |
|---|---|
| [planning/refinement-backlog.md](planning/refinement-backlog.md) | Master backlog: what shipped, what is deferred, release blockers. |
| [planning/install-engine-plan.md](planning/install-engine-plan.md) | Install/uninstall engine chapter plan and status. |
| [planning/ui-design-brief.md](planning/ui-design-brief.md) | What every screen contains and how the app behaves, for redesign work. |

## Release

| Document | Purpose |
|---|---|
| [release/packaging.md](release/packaging.md) | Machine-scope MSI, ProgramData store, publisher line, build script. |
| [release/update-signing.md](release/update-signing.md) | GPG-signed release manifest: key ceremony, signing, verification. |
| [release/hardening-checklist.md](release/hardening-checklist.md) | Binary and process hardening record (CFG, DLL search lockdown, IPC audit). |
| [why-gplv2.md](why-gplv2.md) | Why every component, including the Session 0 service, is GPLv2. |

## Windows Research

| Document | Purpose |
|---|---|
| [windows-settings-registry-map.md](windows-settings-registry-map.md) | Master reference mapping Windows settings to registry keys, with schema, gotchas, and restart requirements. |
| [windows-signal-flow.md](windows-signal-flow.md) | Boot-to-desktop signal flow, registry as control plane, Explorer refresh vs restart, WM_SETTINGCHANGE. |
| [research/epic2-registry-research.md](research/epic2-registry-research.md) | Registry patterns for context menu handler enumeration and the test sandbox key strategy. |
| [research/context-menu-handlers-analysis.md](research/context-menu-handlers-analysis.md) | Classified inventory of context menu handlers (critical/system/optional/third-party) with safe-to-disable guidance. |
| [research/context-menu-catalog.md](research/context-menu-catalog.md) | Visual observation catalog of real context menus cross-referenced to scanner output. |
| [research/static-verb-registry-audit.md](research/static-verb-registry-audit.md) | Static verb registry audit output and the script that produced it ([audit-static-verbs.ps1](research/audit-static-verbs.ps1)). |
| [research/background-handler-surface-detection.md](research/background-handler-surface-detection.md) | Plan for detecting which surfaces a background handler registers on. |
| [research/background-handler-probe-limitations.md](research/background-handler-probe-limitations.md) | COM probe limitations and failed approaches. |
| [research/autoruns-analysis.md](research/autoruns-analysis.md) | Breakdown of an Autoruns export mapped to startup, services, and scheduled task stories. The raw export is not in the repo. |
| [research/sku-restriction-audit.md](research/sku-restriction-audit.md) | Which policies apply on Home/Pro/Enterprise/Education. |
| [research/RESEARCH-PLAN.md](research/RESEARCH-PLAN.md) | How the Autoruns and ShellExView exports were analyzed. |

## Deep Research (External)

| Document | Purpose |
|---|---|
| [deep-research/SUMMARY.md](deep-research/SUMMARY.md) | Cited synthesis of the research set. Read this, not the full documents. |
| [deep-research/index.md](deep-research/index.md) | Index of the full documents: threat modeling, kernel driver security, context menu architecture, control surface mapping, NativeAOT runtime integrity. |
