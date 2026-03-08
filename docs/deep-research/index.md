# Deep Research Index

External research documents produced via deep-research tools (Gemini, etc.) for architectural and implementation decisions. Original PDFs preserved in `pdf-originals/`.

## Security Architecture

| Document | Pages | Purpose |
|---|---|---|
| [threat-modeling-research-part1.md](./threat-modeling-research-part1.md) | 21 | Core component threat modeling: VBS/HVCI implications, kernel trust boundaries, attack surface analysis for a system configuration utility operating under modern Windows 11 security constraints. |
| [threat-modeling-research-part2.md](./threat-modeling-research-part2.md) | 23 | Architectural threat synthesis and external risk analysis: dual-mode trust model (Admin Mode vs Owner Mode), Session 0 service security, PawnIO driver integration threats, supply chain and update integrity. |
| [windows-kernel-driver-security-research.md](./windows-kernel-driver-security-research.md) | 21 | Windows 11 kernel driver landscape: Ring 0 trust model under VBS/HVCI, driver signing requirements (Attestation vs EV), IPC models, hardware abstraction layers, and implications for PawnIO-based hardware control. |

## Windows Platform Research

| Document | Pages | Purpose |
|---|---|---|
| [windows11-context-menu-research.md](./windows11-context-menu-research.md) | 20 | Windows 11 context menu architecture internals: bifurcated menu system (modern vs legacy), IContextMenu COM interface, shell extension isolation, registry registration patterns, rendering pipeline, and third-party integration constraints. Directly relevant to Epic 2. |
| [windows11-control-surface-research.md](./windows11-control-surface-research.md) | 34 | Windows 11 control surface mapping and behavioral friction analysis: comprehensive registry/GPO pathways across all subsystems, enforcement mechanisms that resist user modification (filter drivers, cloud policy caches, telemetry services, scheduled tasks), and user sentiment data for feature prioritization. |

## Original PDFs

Original research PDFs are preserved in [`pdf-originals/`](./pdf-originals/) for verification against the markdown conversions.
