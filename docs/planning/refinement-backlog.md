# Module Refinement Backlog

Gap analysis (2026-08-29) of shipped modules vs. the open-source recipe sources
(Sophia Script 7.2.0 [MIT], CTT winutil [MIT], Melody [CC0], ExplorerPatcher [GPLv2]).
Scope rule for this chapter: **refine existing modules first** — absorb recipes that fit
modules we already ship before building new modules. Recipes port into our
catalogs/sets with full before-state + undo; never shell out to the source tools.

Current coverage baseline: ~40 fixed toggles (Explorer 13, Annoyances 21 cards,
Windows Update 5, Power 1) + 5 built-in sets (59 entries) + the enumerative modules
(Context Menus, Environment, Startup & Services, Power grid).

## 0. Housekeeping — DONE 2026-08-29

- `NotificationSettingsReader` deleted; its unique coverage migrated to Annoyances:
  `lock-screen-ads` upgraded to an atomic pair (overlay + SubscribedContent-338387,
  matching the Settings checkbox), new `lock-screen-images` single, new
  `preinstalled-apps` atomic trio (OEM/PreInstalled/SoftLanding, added to Privacy
  Baseline), new `dynamic-search-box` single (drift-fragile).
- `spotlight-collection-desktop` shipped as an Annoyances single with the Home SKU
  tag (the policy is honored on Enterprise/Education only per the Policy CSP; the
  card description carries the Pro caveat the single-SKU tag cannot express).
- SKU model upgraded to tiers (2026-08-29, Sam's design): `SkuRestriction` now means
  the MINIMUM edition tier honoring the policy — Home(0) < Pro(1) <
  Enterprise/Education(2), `WindowsSku.Tier()`. Spotlight tags `Education`,
  WU/Copilot/Recall/activity-history tag `Pro`. Card callout names the requirement.

## Privacy & Telemetry module — SHIPPED 2026-08-30 (v1, 6 cards)

telemetry-level (AllowTelemetry=1 + DiagTrack companion), error-reporting,
location (Pro tag), app-launch-tracking, handwriting-data-sharing (Pro tag),
inking-typing atomic quartet. Privacy Baseline set updated to match.

Open design decisions carried forward:
- **Directional companions — RESOLVED 2026-08-30** (Sam picked the executor mode):
  `SettingEnforcement.RestoresCompanions` flips the executor sequence — a
  restore-direction change runs primary-then-re-enable (services Disabled →
  Manual); reverting it re-hardens via the disable-shaped sequence. Telemetry
  toggle is now symmetric in every path. Any future companion-service setting
  attaches a configure enforcement plus a RestoresCompanions restore enforcement.
- v2 candidates: cloud clipboard/cross-device policies, Wi-Fi/hotspot reporting,
  diagnostic scheduled-task companions, consent-store location per-app controls.
- Sidebar icon: no "privacy" glyph mapped (same gap as "windows-update") — UI/UX
  chapter.

## Future feature (new-module territory): SKU upgrade via generic keys

Sam's idea: the app detects the edition tier and offers to upgrade the machine's
SKU by redeeming Microsoft's published generic (non-activating) edition-change
keys — the documented prerequisite step before redeeming a real key of the higher
edition (Home → Pro → Education/Enterprise). Mechanics: `changepk.exe /ProductKey`
or slmgr with the generic key triggers the edition-change servicing path. Care
required: edition changes are effectively one-way, involve a reboot, and must be
heavily confirmed — likely paired with the SKU callout ("this setting needs Pro —
upgrade edition?" flow). Belongs with the install-engine chapter, not refinement.

## 0b. UI copy sweep (UI/UX chapter)

Sam's rule: user-visible strings are product copy — short declarative sentences, no
em dashes, no wordy asides. New strings follow it (2026-08-29); the older card
descriptions (Annoyances/WU/Explorer, epics 2-27) still need a one-pass rewrite.

## 1. Explorer module — DONE 2026-08-29 (13 → 26 toggles)

Shipped: item checkboxes, Quick Access recent/frequent, transfer dialog detailed
mode, merge conflicts, restore folder windows, seconds in clock, Task View button,
End Task (TaskbarDeveloperSettings), Start recommendations, Start account
notifications, Snap Assist, Aero Shake. Search entries now derive from the reader.

Follow-up 2026-08-30: shortcut-suffix shipped (string prefs + AbsentValue
delete-to-restore now supported in the Explorer reader and ShellModule).

Deferred remainder (needs a non-toggle control or riskier writes):
- Taskbar search mode hidden/icon/box (`Search\SearchboxTaskbarMode`) and taskbar
  button combining (`ADV\TaskbarGlomLevel`) — multi-state; wait for a choice
  control in the Explorer view (or card ControlType) in the UI/UX chapter
- Recycle Bin delete confirmation — SHELLSTATE binary blob bit, not a value write
- Explorer Home and Gallery pinning (winutil: `System.IsPinnedToNameSpaceTree`
  on two HKCU Classes CLSIDs) — key-presence style write, model like the classic
  context menu toggles
- Folder-type auto-discovery off (shell bags `FolderType=NotSpecified`) —
  destructive Bags/BagMRU reset, one-shot action not a toggle

## 2. Windows Annoyances module — DONE 2026-08-29 (→ 30 cards)

Shipped: consumer-features master policy (Education/Enterprise tag),
tailored-experiences, language-list-access, xbox-game-tips, and the edge-debloat
atomic trio (shopping assistant + Rewards + personalization reporting).

Follow-up 2026-08-30: feedback-frequency shipped (empty AfterValue now restores
by deleting the value, the WU/Power convention).

Deferred remainder:
- Game Bar `UseNexusForGameBarEnabled` (Xbox button binding): needs care, some
  controllers rely on it
- Widgets AppX removal → install/uninstall engine

## 3. Windows Update module — DONE 2026-08-29 (5 → 8 cards)

Shipped as the "Update Experience" group (UX\Settings state values, no GPCache,
no SKU tag): restart-notifications, active-hours-manual, continuous-innovation.

Deferred: include updates for other Microsoft products (needs the Microsoft
Update COM service registration, not a registry write).

## 4. Power Plans module

- Hibernation on/off (winutil/Sophia: `powercfg /hibernate` + `HibernateEnabled`) —
  needs a powercfg interop call, fits existing Power interop
- Ultimate Performance plan install/remove (winutil: `powercfg -duplicatescheme
  e9a42b02-...`) — plan-level action, not a toggle
- Network adapter power saving (Sophia) — NIC device setting, not powrprof; flag
  for design (may belong to a future network module instead)
- Melody `PowerPlans` repo: candidate importable plan definitions (CC0 upstream is
  batch; port as data)
- Note: winutil's S0/S3 items already reachable via our dynamic grid +
  `modern-standby` toggle — no work needed.

## 5. Context Menus module

Add a curated "Windows entries" catalog (named toggles) on top of the enumerative
scanner — all from Sophia's context menu section:
- "Extract all" for MSI; "Install" for CAB
- Edit with Clipchamp / Photos / Paint entries
- "Print" on batch files
- New → Compressed folder
- 15+ file selection verb limit (`EXPL\MultipleInvokePromptMinimum`)
- "Search the Microsoft Store" in Open With (`Policies Explorer\NoUseStoreOpenWith`)
- Open Terminal as admin entry
These mostly use the existing LegacyDisable/ProgrammaticAccessOnly/Blocked
mechanisms the module already implements.

## 6. Clean Boot set cross-check — DONE 2026-08-30

- Added Sophia's remaining Application Experience tasks: ProgramDataUpdater and
  MareBackup (both telemetry/compat inventory uploads).
- winutil's set-to-Manual services (`StorSvc`, `SharedAccess`, `CscService`)
  deliberately NOT added: they are already Manual/trigger-start or Disabled by
  default on Windows 11, so the entries would be no-ops with side-effect risk.
- `SvcHostSplitThresholdInKB` deferred: winutil computes the value from installed
  RAM, so it needs a dynamic-value tweak type; revisit with a performance group.

## Deliberately NOT in this pass (new-module territory)

- Telemetry level / DiagTrack / error reporting → the planned Privacy & Telemetry
  module (already referenced as "arrives later" in Annoyances descriptions)
- Defender, SmartScreen, DoH/DNS, LSA → security module (mind the deep-research
  warning: never write Defender policy hives)
- UWP/AppX debloat, OneDrive removal, Edge removal, VCRedist/.NET installs,
  laptop-OEM tool installs (G-Helper/OmenMon) → install/uninstall engine
  (retired Epics 24/13; BCUninstaller + winutil app list as references)
- File associations, user shell folder relocation, WSL → unscoped
- Visual effects/performance preset (winutil) → decide module home later
