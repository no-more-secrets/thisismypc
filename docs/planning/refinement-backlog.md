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
- **v2 SHIPPED 2026-08-30** (6 → 8 cards): cross-device-clipboard (Pro tag per the
  Privacy CSP), online-speech (consent flag, delete-restore), error-reporting
  upgraded with directional WerSvc companions. Annoyances activity-history
  upgraded from a lone EnableActivityFeed to the full trio
  (+PublishUserActivities +UploadUserActivities, delete-restore) closing the
  winutil depth gap. Neither new Privacy card joins the baseline set: disabling
  online speech kills voice typing, clipboard sync is a feature choice.
- v3 candidates: diagnostic scheduled-task companions (overlaps Clean Boot
  entries — decide ownership first), consent-store per-app location controls,
  Wi-Fi hotspot reporting (verify still exists on current builds).
- Sidebar icon: no "privacy" glyph mapped (same gap as "windows-update") — UI/UX
  chapter.

## Signing decision (Sam, 2026-08-30): no EV certificate for the foreseeable future

Consequences, verified against the code and upstream:
- **Hardware modules stay feasible**: use the upstream-signed PawnIO release
  (what FanControl/LHM ship) — no cert needed on our side. Constraint: never
  fork or patch the driver; consume upstream releases only. Supersedes
  ADR-001's "we EV-sign PawnIO" assumption.
- **Update integrity moves to the GPG path**: replace AuthenticodeUpdateVerifier
  with the planned GPG manifest verifier (offline key, public key hardcoded in
  the binary, per the threat model) as part of release readiness. Unsigned
  packages must never pass the current Authenticode verifier by accident —
  the swap is a prerequisite for the first public release.
- **App signing plan (FINAL, Sam 2026-08-30): SSL.com OV cert under the LLC,
  hardware-token delivery.** The publisher line must show the LLC, so the
  individual-validated Certum Open Source cert is out. Real prices verified
  2026-08-30: SSL.com OV cert $65-75/yr + FIPS USB token + shipping = ~$150-200
  first year, ~$70/yr after; sign locally with the token on release day. Avoid
  SSL.com eSigner (adds $20/mo). Upgrade path if CI cloud signing is ever
  needed: Certum Standard Cloud via SimplySign, flat ~€209/yr, no usage fees.
  Validation uses the LLC registration + DUNS + phone callback; no org-age
  requirement. Certs max ~459 days (CA/B Forum, early 2026) — multi-year buys
  mean mid-term reissues. Azure Trusted Signing unavailable until ~Apr 2029
  (3-year org history). EV unnecessary: driver signing is moot via upstream
  PawnIO and the EV SmartScreen advantage is reportedly gone in 2026.
  SmartScreen reputation builds from download volume regardless. Only
  release-signing material stays private (GPLv2 rule).

## Machine-scope packaging (Sam, 2026-08-30): the app corresponds to the PC, not a profile

Product identity decision — the app is machine-scoped, matching requireAdministrator,
the Session 0 service, and Epic 28's all-profiles HKU\{sid} drift mapping. Release
packaging must follow:
- **Binaries in `C:\Program Files\`** (admin-only write; deep-research DLL-sideloading
  rule). Velopack's default per-user `%LocalAppData%` install violates this — use its
  machine-wide install mode.
- **Mutable state (settings, change DB) in `%ProgramData%\ThisIsMyPC`**, folder created
  and owned by Administrators/SYSTEM with an admin-only DACL. Not hardened `%APPDATA%`:
  users own their profile folders, and ownership beats a DACL (owner can retake
  WRITE_DAC), so a profile-folder lockdown is undoable by user-level malware.
- Keep integrity validation of stored state in the elevated service regardless of
  folder (defense in depth per threat model tm2:120-134).
- One machine, one install, one database — no per-user copies with divergent views
  of machine state.

## AV / SmartScreen readiness (release-readiness chunk, pre-first-release)

The flag risk is Defender PUA heuristics on the app's behavior (elevated +
SYSTEM service + policy writes + DiagTrack disable), not the cert. Standing
mitigations already in place: never write Defender hives (deep-research rule),
everything reversible and user-confirmed. Pre-release process items:
- Submit each release to Microsoft's false-positive/developer portal BEFORE
  publishing; keep the submission contact on the LLC domain.
- VirusTotal scan of release artifacts as a CI canary (fail loud on new
  detections, never auto-publish over one).
- Ship via winget alongside GitHub releases (validation pipeline + users skip
  browser SmartScreen).
- Know the failure mode: a malware verdict on a signed binary can get the CERT
  revoked, killing all releases signed with it. The GPG layer and reproducible
  builds are the recovery story.
- Expect SmartScreen cold starts to partially repeat at each ~15-month cert
  reissue; reputation carry-over between certs is imperfect.

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
- Widgets AppX removal — DONE 2026-08-30 via the install engine's Windows Apps tab

## 3. Windows Update module — DONE 2026-08-29 (5 → 8 cards)

Shipped as the "Update Experience" group (UX\Settings state values, no GPCache,
no SKU tag): restart-notifications, active-hours-manual, continuous-innovation.

Deferred: include updates for other Microsoft products (needs the Microsoft
Update COM service registration, not a registry write).

## 4. Power Plans module

- Hibernation on/off — DONE 2026-08-30 (CallNtPowerInformation, no powercfg shell)
- Ultimate Performance plan install/remove — DONE 2026-08-30 (PowerDuplicateScheme
  + marker description for locale-proof detection; removal refuses while active)
- Network adapter power saving (Sophia) — NIC device setting, not powrprof; flag
  for design (may belong to a future network module instead)
- "Hibernate instead of sleep" preset (Sam's laptop pattern, 2026-08-30): for
  machines where sleep is unreliable and eventually powers off losing work —
  on battery set Sleep-after to never and Hibernate-after to a short timer.
  Both are ordinary DC value-index writes the settings grid already exposes;
  package as a curated preset/set once sets can carry PowerPlan_Setting
  entries. Pairs with the hibernation-toggle warning (the preset depends on
  the hiberfile existing).
- Melody `PowerPlans` repo: candidate importable plan definitions (CC0 upstream is
  batch; port as data)
- Note: winutil's S0/S3 items already reachable via our dynamic grid +
  `modern-standby` toggle — no work needed.

## 5. Context Menus module — Windows entries catalog DONE 2026-08-30

Shipped as the "Windows" tab (9 toggles from Sophia's recipes): MSI Extract all,
CAB Install, New Compressed folder, Edit with Clipchamp/Photos/Paint (Blocked
CLSIDs), Print on batch files (ProgrammaticAccessOnly on the HKCU overlay),
15+ selection verb limit, Store Open With policy. New `Registry_KeyTree`
change type (allowlisted root paths only) covers the verb-key entries;
additive verbs live under the HKCU classes overlay.
- Deferred: "Open Terminal as admin" — Sophia edits Windows Terminal's
  settings.json and launches wt.exe to generate it; not a registry toggle, does
  not fit the before-state/undo model. Revisit only with a file-content change
  design.

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
- Install/uninstall engine v1 SHIPPED 2026-08-30 (Modules.Software: winget
  catalog + inbox-app removal; see `docs/planning/install-engine-plan.md`).
  `winget upgrade` management SHIPPED 2026-08-30 (Updates tab: header-offset
  table parser in WingetService, upgrade: actions through the queue).
  Still there: OneDrive/Edge full removal, VCRedist/.NET bundles, OEM tools
  (G-Helper/OmenMon)
- File associations, user shell folder relocation, WSL → unscoped
- Visual effects/performance preset (winutil) → decide module home later
