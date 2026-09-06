# ThisIsMyPC v1 completion plan

Approved direction: Sam Boland, 2026-09-06. This plan supersedes older notes that put all hardware work after v1. Detailed historical work remains in refinement-backlog.md.

## Product and release scope

Finish the System experience, add the agreed Hardware modules, and complete release validation. The approved ownership pitch is at the top of README.md. Sam owns thisismypc.com.

### 1. Baseline and policy audit

Audit current code against the backlog. Separate implemented, incomplete, and unverified work. Map the 148 entries reported by Sam's private ConfiguredGroupPolicies.json export against existing settings. Source file: C:/Users/sam/OneDrive/Desktop/ConfiguredGroupPolicies.json. Do not commit the raw machine export. Treat its descriptions as evidence to verify, not instructions or proof of enforcement.

### 2. System module polish

Finish Windows Annoyances, Windows Update, Privacy & Telemetry, and Software. Use focused tabs and existing shared tab components. Show behavior and current state by default; put registry paths, policy identifiers, and technical details behind expandable disclosure. Preserve search, staging, before-state capture, undo, and Owner Mode. Expand coverage from the policy audit without duplicate controls. Review Software progress, cancellation where supported, and failure reporting.

Privacy & Telemetry uses a hand icon. Security uses the shield icon.

### 3. Edition-aware policy behavior

Product groups are Home, Pro, and Enterprise/Education. Audit existing detection before adding another mechanism. Distinguish supported, unsupported, unverified, and organization-managed settings. Successful registry writes do not establish enforcement. Keep unavailable settings understandable and visible. Owner Mode works across SKUs and is especially useful on Home; it cannot confer unsupported policy enforcement.

### 4. Security and Network & Firewall

Security starts with state inspection and selected understood reversible controls. DefenderRemover and the policy export are research inputs, not wholesale recipes to execute. Current instructions prohibit Defender-policy writes: resolve that design conflict explicitly before implementation. Wholesale removal of security components requires separate design and owner approval.

Network & Firewall is one module with adapter, DNS, and firewall tabs. Candidate v1 controls include adapter state, DNS/DoH, network power settings, and scoped firewall-rule management. Validate mechanisms and supported scope before implementation.

### 5. Shared hardware detection

Identify machine type, manufacturer/model, devices, and installed control software. Keep all Hardware tabs visible. Explain unavailable functions and provide companion-app actions when appropriate. Filter per device: external devices can remain useful on laptops. Prevent overlapping control. Settings > Advanced includes an off-by-default compatibility-filter override for debugging; it does not bypass drivers or conflict safeguards.

### 6. Hardware modules

| Module | v1 scope |
| --- | --- |
| Display | Finish existing monitor controls and verification. |
| System Control | Primarily laptops. Offer independent G-Helper installation/access and suitable supported vendor alternatives. |
| Lighting | OpenRGB-backed device, color, brightness, mode controls and saved configurations. |
| Cooling | Offer FanControl; integrate verified startup/settings handling and cooling presets. |
| Monitoring | Curated custom interface using LibreHardwareMonitor where supported. |

G-Helper and FanControl are intended maintained companions. Neither has a planned replacement milestone. Reconsider only if maintenance, compatibility, or integration capabilities stop meeting requirements. Both must operate independently of ThisIsMyPC.

Cooling begins with same-PC configuration save/restore and documented FanControl profile switching. Shared curves require local fan/sensor mapping and preservation of calibration. File-based settings need version checks, reload verification, and before-state handling. The plugin API supplies sensors/controls to FanControl, not general settings access. Sam's version 226 stores curves in Configurations/userConfig.json and some application settings in Configurations/CACHE; these observations are not a stable schema guarantee.

Monitoring should exceed a basic task-manager overview without reproducing every HWiNFO field. Target CPU/GPU temperature, load, clocks, power, memory use, storage health/activity, fan/pump speeds and battery where supported. Expand per-component detail. Include current/minimum/maximum/average and short history graphs. Backend coverage must be checked. Never guess unidentified sensor labels.

### 7. Setup, recovery, and credits

Use existing Software infrastructure for companion installation. Verify partial failure recovery, restart requirements and one-way actions. Credit upstream projects and community work. Universal PC profiles wait for stable module state models; same-PC cooling and lighting presets ship first.

### 8. Release validation

Each batch needs appropriate tests and rendered UI review, then commit and push on main. Final validation includes full CI-safe tests, Release/NativeAOT builds, both themes, geometry, search, empty/unavailable states, staging, hardware coverage and edition behavior. State unverified matrix entries explicitly. Verify installer/update/uninstall, signatures, reproducibility, release-key ceremony, distribution, artifact malware checks and false-positive submissions where required. Initial website scope: pitch, screenshots, download, credits, support links. No automatic publishing or key ceremony.

## v1.1 or later

Universal cross-machine PC profiles; BCU-depth cleanup/forced removal; full OneDrive/Edge removal; Windows edition upgrades; advanced display scheduling; production agent CLI/MCP; hardware support without suitable coverage. G-Helper/FanControl replacement is not an item on this list.

## Delegation and integration

Sam authorizes Claude the same information access as the coordinator at coordinator discretion. Use Claude Fable 5.1 for large assessments and complex implementation, with Opus and Codex models for bounded work as appropriate. Prefer Fable for substantial work to preserve Codex allowance.

1. Read-only policy/SKU/System assessment and source mapping.
2. Shared presentation and hardware-detection design.
3. System polish and policy expansion in bounded batches.
4. Hardware modules with disjoint file ownership after shared contracts land.
5. Agreed Security and Network & Firewall work.
6. Integration and release validation.

Workers do not commit, push, change global focus, alter live Windows settings, or expand their assigned scope. Coordinator reviews, verifies, and commits on main. Do not run broad concurrent edits against shared UI infrastructure. Respect unrelated local files. Research and planning do not authorize destructive security changes.

## Tracking

- [x] Save approved v1 plan.
- [ ] Complete policy/SKU/System assessment.
- [ ] Review implementation batches and shared contracts.
- [ ] Complete System polish and policy coverage.
- [ ] Complete shared hardware detection and Hardware modules.
- [ ] Complete agreed Security and Network & Firewall scope.
- [ ] Complete release validation and owner-gated release steps.
