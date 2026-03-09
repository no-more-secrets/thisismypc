# Deep Research Index

External research documents produced via deep-research tools (Gemini, etc.) for architectural and implementation decisions. Original PDFs preserved in `pdf-originals/`.

## Master Summary

| Document | Lines | Purpose |
|---|---|---|
| [SUMMARY.md](./SUMMARY.md) | ~400 | Unified synthesis of all 8 research documents (169 pages) into a single reference covering the Windows 11 trust model, enforcement drivers, IPC/kernel attack surfaces, bytecode interpreter security, supply chain threats, context menu architecture (contribution taxonomy, Explorer pipeline, ghost handlers, surface inheritance, "New" submenu, multi-selection logic, AppModel State Repository, diagnostic probe requirements), configuration surface mapping, and user-mode runtime integrity (CFG/ACG/CIG). |

## Security Architecture

| Document | Pages | Purpose |
|---|---|---|
| [threat-modeling-research-part1.md](./threat-modeling-research-part1.md) | 21 | Core component threat modeling: VBS/HVCI implications, kernel trust boundaries, attack surface analysis for a system configuration utility operating under modern Windows 11 security constraints. |
| [threat-modeling-research-part2.md](./threat-modeling-research-part2.md) | 23 | Architectural threat synthesis and external risk analysis: dual-mode trust model (Admin Mode vs Owner Mode), Session 0 service security, PawnIO driver integration threats, supply chain and update integrity. |
| [windows-kernel-driver-security-research.md](./windows-kernel-driver-security-research.md) | 21 | Windows 11 kernel driver landscape: Ring 0 trust model under VBS/HVCI, driver signing requirements (Attestation vs EV), IPC models, hardware abstraction layers, and implications for PawnIO-based hardware control. |
| [nativeaot-runtime-integrity-research.md](./nativeaot-runtime-integrity-research.md) | 17 | NativeAOT user-mode runtime integrity: CFG/ACG/CIG compatibility with .NET 10 NativeAOT, CsWin32 interop metadata preservation, enforcement timing (IFEO vs API), WDAC supplemental policies, WinUI 3 dynamic code conflicts, and performance analytics. Closes the user-mode hardening gap identified in prior research. |

## Windows Platform Research

| Document | Pages | Purpose |
|---|---|---|
| [windows11-context-menu-research-part1.md](./windows11-context-menu-research-part1.md) | 20 | Windows 11 context menu architecture internals: bifurcated menu system (modern vs legacy), IExplorerCommand/Sparse Manifests, CLSID override, rendering pipeline latency, exhaustive registry location mapping, static verbs vs dynamic COM, inheritance/priority resolution, vendor implementations (Adobe, 7-Zip, WinRAR, OneDrive, Copilot, PowerToys), and programmatic lifecycle management. Directly relevant to Epic 2. |
| [windows11-context-menu-research-part2.md](./windows11-context-menu-research-part2.md) | 16 | Windows 11 context menu deep architecture: contribution taxonomy (hardcoded/canonical verbs, static verbs, dynamic COM, IExplorerCommand/PackagedCom), Explorer filtering pipeline (pre/post-instantiation), IObjectWithSite fallback for backgrounds, ghost handler taxonomy (benign/malignant/architectural), DesktopBackground vs Directory\Background inheritance, "New" submenu ShellNew architecture, multi-selection set intersection logic, and legacy menu topology/cascading. Directly relevant to Epic 2. |
| [windows11-context-menu-research-part3.md](./windows11-context-menu-research-part3.md) | 17 | Windows 11 context menu advanced implementation: AppModel State Repository and declarative manifest scoping (PackagedCom breaks legacy inheritance), NvCplDesktopContext inverted filtering via IObjectWithSite/PIDL chain with fail-open behavior, ghost handler dynamic state evaluation (OneDrive sync roots, WorkFolders MDM/GPO, DesktopSlideshow wallpaper mode, MFS_HIDDEN flag), PackagedCom enumeration via AppExtensionCatalog WinRT API, static verb inheritance for backgrounds, and diagnostic probe architecture requirements. Directly relevant to Epic 2. |
| [windows11-control-surface-research.md](./windows11-control-surface-research.md) | 34 | Windows 11 control surface mapping and behavioral friction analysis: comprehensive registry/GPO pathways across all subsystems, enforcement mechanisms that resist user modification (filter drivers, cloud policy caches, telemetry services, scheduled tasks), and user sentiment data for feature prioritization. |

## Original PDFs

Original research PDFs are preserved in [`pdf-originals/`](./pdf-originals/) for verification against the markdown conversions.
