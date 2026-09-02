# ThisIsMyPC documentation

This folder was written by AI models (Claude Code during development, Gemini
for the deep research) and checked against the code. It is design rationale
and reference, not a spec: where a document and the code disagree, the code
wins. If an agent is working on the repo, it needs the root `CLAUDE.md`
(operating rules, build and test commands, UI verification loop) before
anything here; `planning/refinement-backlog.md` is the plan and status.

## Development

| Document | What it is |
|---|---|
| [avalonia-guide.md](avalonia-guide.md) | Avalonia patterns this app relies on: compiled bindings, Popup limits, command binding inside templates, thread marshaling, NativeAOT constraints, theme and font setup. |
| [sets-schema.md](sets-schema.md) | JSON schema for tweak set definitions, built-in and user. |
| [testing/context-menu-diagnostics.md](testing/context-menu-diagnostics.md) | Diagnostic and integration tests that dump the real registry state for context menus. UI verification itself uses the headless sight harness in `tests/ThisIsMyPC.App.UiTests`. |
| [agentic-tooling.md](agentic-tooling.md) | Claude Code skills, MCP servers, and IDE setup used on this codebase. |

## Planning and release

| Document | What it is |
|---|---|
| [planning/refinement-backlog.md](planning/refinement-backlog.md) | What shipped, what is deferred, and the release blockers. |
| [planning/ui-design-brief.md](planning/ui-design-brief.md) | What every screen contains and how the app behaves, written for redesign work. |
| [release/packaging.md](release/packaging.md) | Per-machine MSI, ProgramData store, publisher line, build script. |
| [release/update-signing.md](release/update-signing.md) | GPG-signed release manifest: key ceremony, signing, verification. |
| [release/hardening-checklist.md](release/hardening-checklist.md) | Binary and process hardening record: CFG, DLL search lockdown, IPC audit. |
| [why-gplv2.md](why-gplv2.md) | Why every component, including the Session 0 service, is GPLv2. |

## Windows reference

| Document | What it is |
|---|---|
| [windows-settings-registry-map.md](windows-settings-registry-map.md) | Registry keys behind the settings the app manages, with validation status, gotchas, and restart requirements. |
| [research/context-menu-scanner-rationale.md](research/context-menu-scanner-rationale.md) | Why the context menu scanner enumerates the surfaces it does, how handlers are classified, what the background-surface probe can and cannot detect. |
| [research/startup-scanner-rationale.md](research/startup-scanner-rationale.md) | The Startup module's two views: the curated tabs, and the Autoruns tab with every location it reads and how it parks disabled items the way Autoruns does. |
| [research/sku-restriction-audit.md](research/sku-restriction-audit.md) | Which policies apply on Home, Pro, Enterprise, and Education. Referenced from the change factories. |
| [research/ExplorerPatcher-STUDY.md](research/ExplorerPatcher-STUDY.md) | Study of ExplorerPatcher (GPLv2): its hooks, registry patterns, and what this project ported or deliberately did not. Conclusion: integrate the recipes, never the hooking. |

## Deep research

`deep-research/` holds ten long research documents generated with Gemini deep
research (threat modeling, kernel driver security, context menu architecture,
Windows 11 control surface, NativeAOT runtime integrity) and a cited synthesis.
Read [deep-research/SUMMARY.md](deep-research/SUMMARY.md); the full documents
exist so its line-number citations resolve. Their findings shaped the
enforcement layer, the Session 0 service, and the hardening work; the code and
`planning/refinement-backlog.md` record what was actually built.

## What is not here

Raw exports from the developer's machine (Autoruns, ShellExView, registry
audits) were used for the research and are kept outside the repo. The BMAD
planning framework and its artifacts (PRD, architecture document, epics,
story files) drove the first development chapter and were removed before
publication; the decisions they produced are encoded in the code, in
`CLAUDE.md`, and in `planning/refinement-backlog.md`. Where a research doc
here still cites an "Epic" or "Story" number, it refers to that retired plan.
