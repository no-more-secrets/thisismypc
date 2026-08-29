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

## Future feature (new-module territory): SKU upgrade via generic keys

Sam's idea: the app detects the edition tier and offers to upgrade the machine's
SKU by redeeming Microsoft's published generic (non-activating) edition-change
keys — the documented prerequisite step before redeeming a real key of the higher
edition (Home → Pro → Education/Enterprise). Mechanics: `changepk.exe /ProductKey`
or slmgr with the generic key triggers the edition-change servicing path. Care
required: edition changes are effectively one-way, involve a reboot, and must be
heavily confirmed — likely paired with the SKU callout ("this setting needs Pro —
upgrade edition?" flow). Belongs with the install-engine chapter, not refinement.

## 1. Explorer module — DONE 2026-08-29 (13 → 26 toggles)

Shipped: item checkboxes, Quick Access recent/frequent, transfer dialog detailed
mode, merge conflicts, restore folder windows, seconds in clock, Task View button,
End Task (TaskbarDeveloperSettings), Start recommendations, Start account
notifications, Snap Assist, Aero Shake. Search entries now derive from the reader.

Deferred remainder (needs a non-toggle control or riskier writes):
- Taskbar search mode hidden/icon/box (`Search\SearchboxTaskbarMode`) and taskbar
  button combining (`ADV\TaskbarGlomLevel`) — multi-state; wait for a choice
  control in the Explorer view (or card ControlType) in the UI/UX chapter
- Recycle Bin delete confirmation — SHELLSTATE binary blob bit, not a value write
- "- Shortcut" suffix (`EXPL\NamingTemplates\ShortcutNameTemplate`, REG_SZ) —
  reader is DWORD-only today
- Explorer Home and Gallery pinning (winutil: `System.IsPinnedToNameSpaceTree`
  on two HKCU Classes CLSIDs) — key-presence style write, model like the classic
  context menu toggles
- Folder-type auto-discovery off (shell bags `FolderType=NotSpecified`) —
  destructive Bags/BagMRU reset, one-shot action not a toggle

## 2. Windows Annoyances module (21 → ~28 cards)

- §0 migration: the 7 orphaned notification entries land here.
- Consumer features master switch (winutil/Sophia:
  `HKLM Policies CloudContent\DisableWindowsConsumerFeatures`) — complements the
  per-CDM toggles; note enforcement/SKU implications (policy key).
- Tailored experiences (Sophia: `Privacy\TailoredExperiencesWithDiagnosticDataEnabled`)
- Feedback frequency (Sophia: `Siuf\Rules\NumberOfSIUFInPeriod`)
- Website language-list access (Sophia: `Control Panel\International\User Profile\HttpAcceptLanguageOptOut`)
- Xbox Game Bar off + Game Bar tips (Sophia: `GameDVR\AppCaptureEnabled` exists;
  add `GameBar\ShowStartupPanel`, `UseNexusForGameBarEnabled`)
- Edge debloat card group (winutil's Edge policy set — pick the safe subset:
  shopping assistant, sidebar app rotation, rewards, personalization; we already
  have `edge-sidebar` + `edge-shortcuts`)
- Widgets: we hide the button; winutil removes the AppX packages — defer removal
  to the install/uninstall engine, keep as note.

## 3. Windows Update module (5 → ~9 cards)

All WU policy territory we already enforce (GPCache clear pattern exists):
- Restart notifications (Sophia: `WU\SetUpdateNotificationLevel` /
  `RestartNotificationsAllowed2`)
- Active hours automatic/manual (Sophia: `WU\AU\SmartActiveHoursState`)
- Get latest updates as soon as available toggle (Sophia:
  `WU\...\IsContinuousInnovationOptedIn`)
- Include updates for other Microsoft products (Sophia: Update service opt-in —
  needs COM (`Microsoft Update` service registration) rather than registry; flag
  for design)

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

## 6. Clean Boot set cross-check (Startup & Services)

- winutil sets services to **Manual** where Clean Boot uses **Disabled** — review
  per-service; Manual is the safer default for `StorSvc`, `SharedAccess`, `CscService`
  (winutil's list) if we add them.
- `SvcHostSplitThresholdInKB` (winutil) — new tweak type (single HKLM value),
  decide module home (Annoyances "performance" group?).
- Sophia's diagnostic scheduled-task list — cross-check against Clean Boot's 8
  task entries for additions.

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
