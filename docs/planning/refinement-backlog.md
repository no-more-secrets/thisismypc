# Module Refinement Backlog

Gap analysis (2026-08-29) of shipped modules vs. the open-source recipe sources
(Sophia Script 7.2.0 [MIT], CTT winutil [MIT], Melody [CC0], ExplorerPatcher [GPLv2]).
Scope rule for this chapter: **refine existing modules first**: absorb recipes that fit
modules we already ship before building new modules. Recipes port into our
catalogs/sets with full before-state + undo; never shell out to the source tools.

Current coverage baseline: ~40 fixed toggles (Explorer 13, Annoyances 21 cards,
Windows Update 5, Power 1) + 5 built-in sets (59 entries) + the enumerative modules
(Context Menus, Environment, Startup & Services, Power grid).

## 0. Housekeeping: DONE 2026-08-29

- `NotificationSettingsReader` deleted; its unique coverage migrated to Annoyances:
  `lock-screen-ads` upgraded to an atomic pair (overlay + SubscribedContent-338387,
  matching the Settings checkbox), new `lock-screen-images` single, new
  `preinstalled-apps` atomic trio (OEM/PreInstalled/SoftLanding, added to Privacy
  Baseline), new `dynamic-search-box` single (drift-fragile).
- `spotlight-collection-desktop` shipped as an Annoyances single with the Home SKU
  tag (the policy is honored on Enterprise/Education only per the Policy CSP; the
  card description carries the Pro caveat the single-SKU tag cannot express).
- SKU model upgraded to tiers (2026-08-29, Sam's design): `SkuRestriction` now means
  the MINIMUM edition tier honoring the policy: Home(0) < Pro(1) <
  Enterprise/Education(2), `WindowsSku.Tier()`. Spotlight tags `Education`,
  WU/Copilot/Recall/activity-history tag `Pro`. Card callout names the requirement.

## Privacy & Telemetry module: SHIPPED 2026-08-30 (v1, 6 cards)

telemetry-level (AllowTelemetry=1 + DiagTrack companion), error-reporting,
location (Pro tag), app-launch-tracking, handwriting-data-sharing (Pro tag),
inking-typing atomic quartet. Privacy Baseline set updated to match.

Open design decisions carried forward:
- **Directional companions: RESOLVED 2026-08-30** (Sam picked the executor mode):
  `SettingEnforcement.RestoresCompanions` flips the executor sequence: a
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
  entries: decide ownership first), consent-store per-app location controls,
  Wi-Fi hotspot reporting (verify still exists on current builds).
- Sidebar icons: DONE 2026-08-30 (privacy shield, windows-update refresh arrow,
  environment chevrons).

## Signing decision (Sam, 2026-08-30): no EV certificate for the foreseeable future

Consequences, verified against the code and upstream:
- **Hardware modules stay feasible**: use the upstream-signed PawnIO release
  (what FanControl/LHM ship): no cert needed on our side. Constraint: never
  fork or patch the driver; consume upstream releases only. Supersedes
  ADR-001's "we EV-sign PawnIO" assumption.
- **Update integrity: GPG manifest verifier SHIPPED 2026-08-31.**
  GpgManifestUpdateVerifier replaces AuthenticodeUpdateVerifier: each release
  publishes SHA256SUMS + detached SHA256SUMS.asc (offline key), the app embeds
  the public key and fail-closes on everything (no manifest, bad signature,
  digest mismatch, unresolved package path; the old verify-own-binary fallback
  is gone). Embedded key is EMPTY until Sam runs the key ceremony
  (docs/release/update-signing.md; a pinned test flips with the key commit),
  so every update is rejected until then: correct direction, but the ceremony
  is a release blocker. Tooling: tools/new-release-manifest.ps1. Release tags
  must be v{version}.
- **App signing plan (FINAL, Sam 2026-08-30): SSL.com OV cert under the LLC,
  hardware-token delivery.** The publisher line must show the LLC, so the
  individual-validated Certum Open Source cert is out. Real prices verified
  2026-08-30: SSL.com OV cert $65-75/yr + FIPS USB token + shipping = ~$150-200
  first year, ~$70/yr after; sign locally with the token on release day. Avoid
  SSL.com eSigner (adds $20/mo). Upgrade path if CI cloud signing is ever
  needed: Certum Standard Cloud via SimplySign, flat ~€209/yr, no usage fees.
  Validation uses the LLC registration + DUNS + phone callback; no org-age
  requirement. Certs max ~459 days (CA/B Forum, early 2026): multi-year buys
  mean mid-term reissues. Azure Trusted Signing unavailable until ~Apr 2029
  (3-year org history). EV unnecessary: driver signing is moot via upstream
  PawnIO and the EV SmartScreen advantage is reportedly gone in 2026.
  SmartScreen reputation builds from download volume regardless. Only
  release-signing material stays private (GPLv2 rule).

## Machine-scope packaging: SHIPPED 2026-08-31 (docs/release/packaging.md)

The app corresponds to the PC, not a profile. Implemented:
- **All mutable state now lives in `%ProgramData%\ThisIsMyPC`** (settings,
  history.db, sets, monitoring, drift baseline): AppConstants collapsed to one
  DataDirectoryPath; DACL-hardened at startup; LegacyDataMigration copies old
  `%APPDATA%\ThisIsMyPC` data across once and marks the old folder.
- **tools/build-release.ps1** publishes App + Service into one staging dir and
  packs the Velopack per-machine MSI (`--msi --instLocation PerMachine`, WiX 5,
  Program Files, elevation required). Per-user Setup.exe and portable zip are
  not shipped.
- Open at release: MSI publisher line needs the final LLC name; Authenticode
  signing on release day; UpdateUrl org must match the publishing repo.
- Keep integrity validation of stored state in the elevated service regardless of
  folder (defense in depth per threat model tm2:120-134).

## UI Gallery (dev-facing, added 2026-08-30)

One-page style reference: type scale, color tokens, text tiers, every
standardized control class (toolbar-toggle, apply-bar, status text, scope
badges, pending markers, notification bars). Sidebar bottom, above Settings.
Debug builds only since 2026-08-31 (compile-time gate on the sidebar button);
Release also drops the Debug-only log console window. Nothing left for release
prep here.

## Repo publication hygiene (release blocker: the repo is private today, goes public GPLv2)

- **TWEAKS.md moved out 2026-08-31**: the personal machine audit now lives at
  `C:\Users\user\Dev-Projects\laptop-debloat\TWEAKS.md` (only copy; back it up)
  and is gitignored so it cannot come back.
- **Git history still contains it** (entered at ada10cb) along with whatever
  else `_bmad-output/` history holds. Before flipping the repo public, either
  scrub history (`git filter-repo` on the file paths) or publish with fresh
  history; do a full history review for personal data either way. Sam's call
  which; scrubbing rewrites every SHA, so do it after development quiets, right
  before publication.
- **Owner mismatch to resolve at publication**: `AppConstants.UpdateUrl` points
  at `github.com/No-More-Secrets/thisismypc`; the actual remote is
  `github.com/samboland/thisismypc` (private). The update verifier derives its
  manifest URL from UpdateUrl, so whichever org/name ships must match where
  releases are published. Naming is Sam's decision.

## Binary hardening: Sam's final checklist worked 2026-08-31

Full item-by-item record: `docs/release/hardening-checklist.md`. Verified: CFG/
DEP/ASLR/CET/EHCONT on both CoreCLR and AOT exes (AOT needed ControlFlowGuard
enabled; dumpbin-proven), IPC boundary audited against tm1 (all prescriptions
already implemented). Implemented but NOT yet built/tested/committed (session
lost shell access): SetDefaultDllDirectories at both entry points, WinVerifyTrust
gate on winget (alias reparse-target resolution + Microsoft signer check). Run
the verification debt commands in that doc first thing next session.

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
keys: the documented prerequisite step before redeeming a real key of the higher
edition (Home → Pro → Education/Enterprise). Mechanics: `changepk.exe /ProductKey`
or slmgr with the generic key triggers the edition-change servicing path. Care
required: edition changes are effectively one-way, involve a reboot, and must be
heavily confirmed: likely paired with the SKU callout ("this setting needs Pro:
upgrade edition?" flow). Belongs with the install-engine chapter, not refinement.

## 0d. UI/UX chapter closers: DONE 2026-08-31

- **In-app toasts**: ToastStackViewModel + ToastHostControl overlay top-right in
  MainWindow (severity accent bar, close button, 6 s auto-dismiss, cap 4, oldest
  yields). Gated notifications (9-2) route here; the status bar stays reserved
  for the apply pipeline.
- **Actionable Owner Mode callout + observable degradation**: degraded cards now
  carry a Turn on Owner Mode button (IOwnerModeLifecycle, implemented by
  OwnerModeService) and refresh live on StateChanged; enabling the service
  un-degrades every visible card to the badge state. Callout copy no longer
  claims the service is unshipped. No shipped card sets OwnerModeRequired yet;
  sets can, and the path is screenshot-tested (owner-mode-card suite).
- **Search lands on the card**: selecting a cross-module search result pre-fills
  the target page's own search box (ISearchFocusTarget: Explorer, Annoyances,
  Privacy, Windows Update), so the matched card is the page content on arrival.
  Pages without per-setting filters keep the status-bar fallback.

## 0a2. Light theme: DONE 2026-08-31

Palette moved to theme dictionaries (Styles/Theme.axaml, Dark + Light variants,
every key in both), all consumers swept to DynamicResource, ThemeService applies
the persisted choice at startup and live from Settings (Dark / Light / Follow
Windows). Sight harness: UiSession.SetTheme + light walkthrough suite
(walkthrough-light/). Drag ghost and insert line in PathEditorView resolve theme
brushes at use time; RowHoverBrush replaces hardcoded hover whites.

## 0c. Interaction consistency + outline style: DONE 2026-08-30

One token family for every clickable (Outline/OutlineStrong, ControlHover/
ControlPressed, AccentHover/AccentPressed) wired through Fluent resource-key
overrides in Theme.axaml, so stock controls and custom classes share hover,
press, and disabled states. Everything gets a 1px outline: buttons, chips,
combo boxes, text inputs, and all Surface cards (new Border.card class, 23
sites converted). ToggleSwitch now uses the app accent, not the Windows one.

## 0b. UI copy sweep: DONE 2026-08-30

Sam's rule: user-visible strings are product copy; short declarative sentences, no
em dashes, no wordy asides. Swept: Settings page (stale Epic 9 claims, dead Module
Preferences placeholder), Explorer title-restating descriptions rewritten to carry
real information, Annoyances/WU tightened, every em dash purged from src (321 in
strings and comments). Docs and tests still carry em dashes; purge on touch.

## 1. Explorer module: DONE 2026-08-29 (13 → 26 toggles)

Shipped: item checkboxes, Quick Access recent/frequent, transfer dialog detailed
mode, merge conflicts, restore folder windows, seconds in clock, Task View button,
End Task (TaskbarDeveloperSettings), Start recommendations, Start account
notifications, Snap Assist, Aero Shake. Search entries now derive from the reader.

Follow-up 2026-08-30: shortcut-suffix shipped (string prefs + AbsentValue
delete-to-restore now supported in the Explorer reader and ShellModule).

Multi-choice rows SHIPPED 2026-08-31: ShellChoiceSettingViewModel +
ChoiceSettingRowTemplate (combo row, pending-modify tint, same staging
lifecycle as toggles). Taskbar search mode (SearchboxTaskbarMode 0-3, Win11
names) and taskbar button combining (TaskbarGlomLevel 0-2) shipped on it,
both Modify-category with ExplorerRestart. The shared card renderer's
Dropdown ControlType (SettingCardModel already declares it) stays
unimplemented until a card module needs one; first candidate is a
telemetry-level card (AllowTelemetry 0-3) in Privacy.

Deferred remainder (needs a non-toggle control or riskier writes):
- Recycle Bin delete confirmation: SHELLSTATE binary blob bit, not a value write
- Explorer Home and Gallery pinning (winutil: `System.IsPinnedToNameSpaceTree`
  on two HKCU Classes CLSIDs): key-presence style write, model like the classic
  context menu toggles
- Folder-type auto-discovery off (shell bags `FolderType=NotSpecified`):
  destructive Bags/BagMRU reset, one-shot action not a toggle

## 2. Windows Annoyances module: DONE 2026-08-29 (→ 30 cards)

Shipped: consumer-features master policy (Education/Enterprise tag),
tailored-experiences, language-list-access, xbox-game-tips, and the edge-debloat
atomic trio (shopping assistant + Rewards + personalization reporting).

Follow-up 2026-08-30: feedback-frequency shipped (empty AfterValue now restores
by deleting the value, the WU/Power convention).

Deferred remainder:
- Game Bar `UseNexusForGameBarEnabled` (Xbox button binding): needs care, some
  controllers rely on it
- Widgets AppX removal: DONE 2026-08-30 via the install engine's Windows Apps tab

## 3. Windows Update module: DONE 2026-08-29 (5 → 8 cards)

Shipped as the "Update Experience" group (UX\Settings state values, no GPCache,
no SKU tag): restart-notifications, active-hours-manual, continuous-innovation.

Deferred: include updates for other Microsoft products (needs the Microsoft
Update COM service registration, not a registry write).

## 4. Power Plans module

- Hibernation on/off: DONE 2026-08-30 (CallNtPowerInformation, no powercfg shell)
- Ultimate Performance plan install/remove: DONE 2026-08-30 (PowerDuplicateScheme
  + marker description for locale-proof detection; removal refuses while active)
- Network adapter power saving (Sophia): NIC device setting, not powrprof; flag
  for design (may belong to a future network module instead)
- "Hibernate instead of sleep" preset (Sam's laptop pattern, 2026-08-30): for
  machines where sleep is unreliable and eventually powers off losing work:
  on battery set Sleep-after to never and Hibernate-after to a short timer.
  Both are ordinary DC value-index writes the settings grid already exposes;
  package as a curated preset/set once sets can carry PowerPlan_Setting
  entries. Pairs with the hibernation-toggle warning (the preset depends on
  the hiberfile existing).
- Melody `PowerPlans` repo: candidate importable plan definitions (CC0 upstream is
  batch; port as data)
- Note: winutil's S0/S3 items already reachable via our dynamic grid +
  `modern-standby` toggle: no work needed.

## 5. Context Menus module: Windows entries catalog DONE 2026-08-30

Shipped as the "Windows" tab (9 toggles from Sophia's recipes): MSI Extract all,
CAB Install, New Compressed folder, Edit with Clipchamp/Photos/Paint (Blocked
CLSIDs), Print on batch files (ProgrammaticAccessOnly on the HKCU overlay),
15+ selection verb limit, Store Open With policy. New `Registry_KeyTree`
change type (allowlisted root paths only) covers the verb-key entries;
additive verbs live under the HKCU classes overlay.
- Deferred: "Open Terminal as admin": Sophia edits Windows Terminal's
  settings.json and launches wt.exe to generate it; not a registry toggle, does
  not fit the before-state/undo model. Revisit only with a file-content change
  design.

## 6. Clean Boot set cross-check: DONE 2026-08-30

- Added Sophia's remaining Application Experience tasks: ProgramDataUpdater and
  MareBackup (both telemetry/compat inventory uploads).
- winutil's set-to-Manual services (`StorSvc`, `SharedAccess`, `CscService`)
  deliberately NOT added: they are already Manual/trigger-start or Disabled by
  default on Windows 11, so the entries would be no-ops with side-effect risk.
- `SvcHostSplitThresholdInKB` deferred: winutil computes the value from installed
  RAM, so it needs a dynamic-value tweak type; revisit with a performance group.

## Known test flake (fix during release prep)

`StaticVerbIntegrationTests.Sandbox_scan_reads_all_metadata` fails intermittently
(~1 in 3) when the full Integration suite runs, always passes alone: registry
sandbox contention with a parallel test writing the same HKCU sandbox area.
Predates 2026-08-31. Fix: unique per-test sandbox key or a collection fixture
serializing the sandbox users.

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

## Display module: v1 SHIPPED 2026-08-30 (first Hardware-group module)

Absorbed Twinkle Tray's core, lean scope: per-monitor brightness/contrast
sliders and input switching over DDC/CI (dxva2, fresh handles per operation so
display changes never strand a stale handle), input options parsed from the
MCCS capabilities string (VcpCapabilities in Core, unit-tested), laptop panel
as a synthetic row through the active power plan's VIDEO brightness setting
(no WMI, NativeAOT-safe, gated on GetSystemPowerStatus battery presence).
Controls apply LIVE: documented carve-out from the pending pipeline
(ephemeral hardware state, sliders are their own undo).

Also ported (2026-08-31): real monitor names from EDID, session-scoped DDC
re-apply after resume/display change (input source and power mode excluded),
linked multi-monitor brightness (fraction-of-range, toggle appears with 2+
DDC displays), per-monitor Screen off via VCP 0xD6 (button only when the
capabilities string declares it; prefers soft off 04, then 05, then standby
02; never enters the re-apply memory).

Scan speed: the capabilities string and the answering-vendor-code set are
cached per session (keyed monitor id + model name), so only the first
Display visit pays the multi-second DDC probe. Deferred: persist that
cache to disk keyed by model if the first visit ever matters.

Deferred (install Twinkle Tray from the catalog for these): time-of-day
scheduling, sun-based dimming, hotkeys, idle dimming.

**Tray quick-controls flyout (Sam, 2026-08-31): NOT a Twinkle Tray port.**
The tray flyout is the app's shared hardware quick-controls surface; it will
carry brightness now and RGB modes / fan profiles later, on the existing
TrayService. Design it once as a platform when a second hardware module
exists; modules contribute controls to it.

## Hardware chapter doctrine (Sam, 2026-08-30): modules derive from device analysis

Monitor, fan, and RGB control are not three flat modules; a device-analysis
layer picks the backend per machine. On a Strix laptop the ASUS ATKACPI/WMI
platform serves all three (the G-Helper approach); on a ROG-parts desktop each
capability needs its distinct generic service: DDC/CI (dxva2 VCP codes) for
monitors, LibreHardwareMonitor (MPL-2.0; PawnIO driver, never blocklisted
WinRing0) for fans, OpenRGB SDK server protocol (GPLv2, license match) for
lighting. CapabilityDetector's hardware probes are the seed; modules consume
detection and select backends, never assume one.

Licensing: G-Helper is GPL-3.0, incompatible with this GPLv2 repo for code
porting; treat it as protocol documentation only. Twinkle Tray (MIT) and
OpenRGB (GPLv2) are portable/compatible.

Ordering: DDC/CI ships pre-release (driverless Win32). Fans and RGB are
post-release; fans last (kernel driver + AV reputation risk).

## ExplorerPatcher gap analysis (2026-08-31): mined out; integrate, don't imitate

Full EP feature inventory reviewed against our coverage. Verdict: EP's
headline surface (Win10 taskbar, Win10 Start, Alt-Tab styles, tray flyouts,
menu skinning) is injection-only via the C:\Windows\dxgi.dll sideload; since
24H2 removed the legacy code paths, EP ships its own taskbar reimplementation
(ep_taskbar.*.dll) whose source is unpublished. None of that is portable;
EP is a Software-catalog install for users who want the Win10 shell.

Portable remainder to add (small batch on existing infrastructure):
- Win+X shows Command Prompt instead of PowerShell (DontUsePowerShellOnWinX,
  Advanced key): hidden native toggle
- Desktop build watermark (PaintDesktopVersion): minor toggle
- Start layout density (Advanced\Start_Layout: 1 more pins / 0 default /
  2 more recommendations): multi-choice row (not from EP; same family)

Verification needed: MS removed the classic command bar / ribbon code in
24H2. Our "Use classic command bar" CLSID toggle may be inert on current
builds; test on a machine without EP injected (EP's taskbar/shell hooks mask
stock behavior; Sam's desktop runs EP) and add a "no effect on 24H2+"
callout or retire the card.

**Detected-tool integration (Sam, 2026-08-31): post-release candidate.**
EP persists every setting as plain registry under HKCU\Software\
ExplorerPatcher, and its GPLv2 source documents their meanings; that is
inspectable state with a documented contract, so a curated EP settings
section can ride the normal pending-changes pipeline (before-state, staged
review, undo) with zero process control. Shape: detect EP installed, expose
curated cards, note Explorer-restart needs, tolerate per-version value
renames, and warn when values are residue of an uninstalled EP. The
generalized doctrine: integrate tools whose state is inspectable and
documented (EP, OpenRGB SDK); install-but-never-drive opaque ones
(FanControl: closed source, undocumented JSON config, CLI-only control
= the banned shell-out; fans arrive post-release via our own LHM+PawnIO
module).

## Agent interface (Sam, 2026-08-31): CLI first; deferred until the app is fully featured

Decision: users bring their own coding agent (Claude Code etc.) and drive
ThisIsMyPC through a CLI; an MCP server, if ever, is a thin wrapper over the
same commands, never a second implementation. Deferred until the app is
fully featured and the refinement chapters have matured; do not start
pre-release.

Shape agreed in brainstorm:
- **Agent proposes, human disposes.** CLI surface: list modules, scan, read
  state, stage changes, stage one-way actions, read the pending queue. NO
  apply command by default; staged work appears in the GUI's normal review
  bar and the human applies there. This is the prompt-injection story: the
  agent cannot do anything the user does not approve on screen.
- Transport: small unelevated CLI process talking to the elevated app over
  the hardened named pipe (28-1 envelope; extend, never change).
- Exchange format: set definitions. An agent authors a set file, the app
  gives preview + before-state + undo for free; also the community
  share-my-setup format.
- Free win to consider early: read-only diagnostics (agent reads scan data
  to answer "why is my context menu slow").
- Possible later opt-in: a "trusted apply" mode for full automation; ship
  without it first.
