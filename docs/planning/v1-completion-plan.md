# ThisIsMyPC v1 completion plan

Approved direction: Sam Boland, 2026-09-06. This plan supersedes older notes that put all hardware work after v1. Detailed historical work remains in refinement-backlog.md.

Owner Mode scope update: [single-user completion checkpoints](owner-mode-single-user-plan.md). Near-term work targets one primary user; multi-profile restoration is deferred.

## Policy coverage requirement (2026-10-07)

Sam requires editable controls for all practical, useful policies already configured on his PC, plus other important policies.
The 148-record export is the starting inventory, not a ceiling or a recommended configuration to copy.
Use Sam's PC as a validation fixture for useful tweaks and alternate detection paths.
Audit all installed policy definitions and configured values beyond the export, including browser policies and security settings outside ADMX.
Do not wait for individual control failures before checking policy overrides across the other controls.
The [expanded audit](../research/policy-audit-2026-10-07.md) records the first full installed-definition scan and remaining evidence gaps.
All 148 identities match installed ADMX definitions; 36 have additional option fields. Identity matching does not establish current enforcement.
The existing source audit reports 14 direct routes, one partial route, 16 related preferences, and 117 missing routes.

Place controls with their feature in the existing modules. A configured-policy index may link to the same controls.
Represent overlapping preference and policy settings together, with their separate scopes and effects visible.
Do not silently replace a user preference with a machine policy, or equate a missing preference with an enabled feature.

Coverage includes promotions and suggestions, Search and Start, privacy and diagnostics, Windows Update and delivery,
AI features, Explorer and desktop behavior, power, and supported security policies.
Review important additions for update version control, delivery bandwidth, diagnostic data, advertising,
cloud integration, application reputation, unwanted-app protection, ransomware protection, and firewall behavior.
These are coverage candidates, not instructions to enable or disable them on Sam's PC.

For every inventory record, record its feature destination and one disposition: supported control, equivalent existing control,
obsolete, unsupported on this Windows version/edition, or unresolved pending evidence. Give a reason and source for exclusions.
Low-level options may live under Details; complexity alone does not justify silently dropping a configured policy.

Policy controls must support Not configured, Enabled, Disabled, and applicable options using verified policy semantics.
Saved local policy and applied registry state must both be understood. A registry-only write must not be presented as an edit to saved Group Policy.
Show policy source and conflicts where detectable; distinguish configured state from verified effective behavior.
Changes use staging, exact before-state capture, and undo, including absent values and multi-value policies.
Do not broaden Owner Mode's automatic restoration catalog as a side effect of adding policy controls.

Audit each existing control's detection and writes alongside policy integration. Test alternate valid states, bit fields,
missing values, policy overrides, unrelated-option preservation, and exact undo.
Sticky Keys and Filter Keys now preserve unrelated flags and detect the shortcut bit, with automated state and rollback checks.
The remaining control audit is tracked in [correctness checks](../research/control-correctness-checks.md).

Implementation remains pending. First resolve overlapping promotion, Search, and privacy controls; then expand the remaining feature groups.

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

Security starts with state inspection and understood reversible controls. The October 7 policy requirement includes supported security-policy controls.
DefenderRemover and the policy export remain research inputs, not wholesale recipes to execute. Validate supported mechanisms and report tamper-protection refusals.
Adding controls does not authorize changing live security settings during development. Wholesale removal of security components requires separate design and owner approval.

Network & Firewall is one module with adapter, DNS, and firewall tabs. Candidate v1 controls include adapter state, DNS/DoH, network power settings, and scoped firewall-rule management. Validate mechanisms and supported scope before implementation.

### 5. Shared hardware detection

Identify machine type, manufacturer/model, devices, and installed control software. Keep all Hardware tabs visible. Explain unavailable functions and provide companion-app actions when appropriate. Filter per device: external devices can remain useful on laptops. Prevent overlapping control. Settings > Advanced includes an off-by-default compatibility-filter override for debugging; it does not bypass drivers or conflict safeguards.

### 6. Hardware modules

| Module | v1 scope |
| --- | --- |
| Display | Finish existing monitor controls and verification. |
| System Control | Primarily laptops. Offer independent G-Helper installation/access and suitable supported vendor alternatives. |
| Lighting | Built-in controllers ported from OpenRGB (HID and GPU I2C): device, color, brightness and mode controls; saved configurations later. |
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

## Current priority: finish the installer (2026-09-21)

The Win32/GDI installer is merged on main at `829f27a`. Claude Code owns the remaining installer work.
The four workstreams below proceed independently. Installed release validation depends on the completed package.

- [x] Replace Avalonia in the installer and restore keyboard navigation.
- [x] Use standard Windows controls and system colors with native names, roles, and checked states. Replace custom cards and tabs with wizard navigation.
- [x] Keep required controls reachable on smaller displays at higher scaling through scrolling and focus visibility.
- [x] Fix clipped installed-version text and inaccurate removal copy.
- [x] Verify native controls, rendered pages, the full test suite, and guarded NativeAOT output after these changes.
- [ ] Verify Narrator/UIA discovery, high-contrast rendering, and dragging between monitors with different scaling in the signed installer.
- [ ] Build a complete signed test package under the pinned release toolchain.
- [ ] Verify fresh installation, upgrade, same-version reinstall, downgrade refusal, and removal with preserved user data.

The exact Build Tools `18.9.12120.119` and linker `14.51.36256.0` are restored beside the newer development IDE.
Original release preflight passes. Release scripts select the exact tools; a frozen local update channel preserves their version.
The verified offline archive also preserves SDK recovery. See [pinned toolchain](../release/pinned-toolchain.md).
CKA 1.1.2 and CodeSignTool 1.3.3 passed their pinned checks. No release pins changed.
Signing requires the owner's secure credential prompt. Automated tests do not substitute for the installed release checks.
Use [installer acceptance](../release/installer-acceptance.md) for the package and user-facing verification steps.

## Active application workstreams (2026-09-21)

Sam approved these four workstreams while Claude Code finishes the installer.

1. **Cooling activation.** Add profile activation after saving, with evidence that FanControl loaded the intended settings.
   Verify active curve assignments and physical fan response, then restore the original configuration.
   A successful process exit or `CACHE.CurrentConfigFileName` is not acknowledgement.
   Preserve calibration, capture the previous runtime selection where possible, and report unsupported versions or service modes explicitly.
   Approved fallback implemented: request the saved profile switch, then require the user to inspect and confirm FanControl's curves.
   Automatic active-curve readback remains unavailable. File undo does not restore an unknown running profile.
2. **Monitoring.** Connect the hardware tab to a read-only sensor backend and build the curated sensor interface.
   Verify LibreHardwareMonitor compatibility with NativeAOT and release security before selecting its integration boundary.
   Include current/minimum/maximum/average readings, short history, and honest unavailable states. Do not add fan control writes.
   The initial implementation uses a narrow LibreHardwareMonitor source port for NVIDIA, plus Windows memory and battery APIs.
   Its guarded NativeAOT probe passes. CPU, motherboard, storage, AMD/Intel GPU coverage and physical battery checks remain pending.
3. **System modules.** Audit policy coverage and edition behavior, then finish settings presentation.
   Reproduce and resolve the recorded Privacy & Telemetry walkthrough timeout.
   Map the private policy export without committing machine data or treating registry writes as proof of enforcement.
4. **Installed release checks.** Use Claude Code's completed package to verify hardware operations, Owner Mode, undo, and module behavior.
   Test under production security restrictions; DebugRelease results do not establish signed-release behavior.
   Record exact build identity, before-state, observed result, and restoration for each live mutation.

Implementation batches require focused tests, UI screenshots where applicable, and independent review before committing.
Keep installer files outside these application batches. Track findings and completion in `refinement-backlog.md`.

## Tracking

- [x] Save approved v1 plan.
- [ ] Complete policy/SKU/System assessment.
- [ ] Review implementation batches and shared contracts.
- [ ] Complete System polish and policy coverage.
- [x] Complete shared hardware detection and the four companion tabs ([hardware-compatibility.md](hardware-compatibility.md)). Physical laptop coverage remains unverified.
- [x] Add native Lighting controls for supported devices, a color picker, and per-device save preferences.
- [x] Add Cooling profile editing with captured file contents for undo.
- [ ] Complete Hardware validation, Cooling profile activation, and Monitoring sensors. Wider Lighting device support remains separately tracked.
- [ ] Complete agreed Security and Network & Firewall scope.
- [ ] Complete release validation and owner-gated release steps.
