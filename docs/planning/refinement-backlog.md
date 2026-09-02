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
- **OV cert PURCHASED 2026-09-01**; identity validation underway, token
  expected by about 2026-09-08. On arrival: install the token driver, confirm
  the cert shows in `Cert:\CurrentUser\My` with a reachable private key,
  test-sign a scratch exe, then `build-release.ps1 -SignThumbprint`. Signing is
  outer-installer-only so its terminal certificate table can be removed for an
  exact comparison with an independent unsigned build. The GPG manifest covers
  the unsigned embedded payload and update assets.
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
- **Installer shipped 2026-09-01** (`src/ThisIsMyPC.Installer`): the download is
  `ThisIsMyPC-Installer.exe`, an elevated NativeAOT Avalonia launcher with the MSI
  inside. Reason: a bare per-machine MSI gets its UAC prompt parked in the
  taskbar and its follow-up dialog can land off screen (Sam, 4K display), and
  the Velopack wizard has no options. Pages: Welcome, GPLv2 license with
  accept, Options (folder, Desktop shortcut, start with Windows, update
  checks), Installing, Done. Trusted files go under the hardened ProgramData
  folder, never %TEMP%. First end-to-end unsigned build ran the same day.
  Sam ran the AOT 0.1.0 and 0.1.1 builds on 2026-09-02: fresh install and
  update in place both clean, install time noted as fast. The Welcome page
  gained an Uninstall checkbox the same day (ticked: Welcome and Remove tabs
  only). Open: the in-app half of the AOT pass (every module page, one
  apply, Owner Mode enable), then flip the script default to AOT.
- Publisher settled 2026-09-01: NMS, short for No More Secrets, LLC (build
  script, assembly metadata, packaging.md); cert subject is the full name. Open at release: Authenticode signing on release
  day; UpdateUrl org must match the publishing repo.
- Keep integrity validation of stored state in the elevated service regardless of
  folder (defense in depth per threat model tm2:120-134).

## UI Gallery (dev-facing, added 2026-08-30)

One-page style reference: type scale, color tokens, text tiers, every
standardized control class (toolbar-toggle, apply-bar, status text, scope
badges, pending markers, notification bars). Sidebar bottom, above Settings.
Debug builds only since 2026-08-31 (compile-time gate on the sidebar button);
Release also drops the Debug-only log console window. Nothing left for release
prep here.

## Repo publication hygiene (public since 2026-09-01, GPLv2)

- **Personal machine audit (TWEAKS.md)** lives outside the repo and is
  gitignored so it cannot come back.
- **History rewritten before publication (2026-09-01)**: one author
  identity, machine dumps and the personal audit purged from every commit,
  hostname and user paths replaced, retired framework trees dropped. Verified
  by a clean build and the full CI suite before the first push.
- **Serilog replaced by NLog 6.2 (BSD-3-Clause) 2026-09-01**: the nuspec
  audit found Serilog and its sinks are Apache-2.0, which FSF treats as
  incompatible with GPLv2, and the project is v2-only. `LoggingSetup`
  (App/Services) builds the NLog config in code: daily JSON file under
  `%ProgramData%\ThisIsMyPC\logs` in the old CLEF shape (@t, @mt, @l, @x,
  properties at the root), 10 MB cap, 7 files kept, Interop loggers Warn+ in
  Release, console target for the Debug log window; since 2026-09-02 the
  Debug run also mirrors into the attached debugger (VS Output window), and
  the main window logs every status line, every module apply/revert/action
  result (category, message, exception, elapsed time), the apply batch
  summary, toasts, and unhandled exceptions; `PowerService` logs every
  refused powrprof call with its Win32 code, so an error is copied from the
  log instead of screenshotted. Every shipped package is
  now MIT or BSD; xunit is Apache-2.0 but test-only, never distributed.
- **Group Policy pin on the active power plan (resolved 2026-09-02)**:
  winutil leaves the ActivePowerScheme policy value (HKLM Policies, Microsoft,
  Power, PowerSettings) naming its plan, and the power service then answers
  every PowerSetActiveScheme with 1260 until the next restart. Measured on
  Sam's PC with the debug log as the instrument: the registry named the target
  and machine policy had just processed (event 1502), yet the target got 1260
  while the cached plan got 0; moving the pin, a subkey nudge under the key,
  RefreshPolicyEx, and removing the pin outright all changed nothing within 14
  seconds. umpoext.dll holds the key string and registers both
  RegisterGPNotification and RegNotifyChangeKeyValue, but reads the pin at
  startup only. Product answer: the scan reports the pin (registry) and the
  lock (PowerSettingAccessCheck ACCESS_ACTIVE_SCHEME = 1260); a switch while
  locked is recorded where the service reads at startup (pin moved to the
  target, or the startup active scheme when the pin is already gone) and the
  change carries RestartRequirement.Reboot; System power gains "Pin the
  active plan by policy", a switch that removes the value (restart). Error
  1260 maps to ProtectedByPolicy, so the guidance no longer blames
  TrustedInstaller. IPolicyRefreshService and its retries are gone. Owed: Sam's
  restart to confirm the pinned target comes up active. Domain-managed pins
  are post-release (the switch would remove a pin the domain re-applies).
- **Package advisories cleared 2026-09-01**: Microsoft.Data.Sqlite 10.0.3
  pulled SQLitePCLRaw 2.1.11 (GHSA-2m69-gcr7-jv3q); bumped to 10.0.11, which
  resolves 2.1.12. Avalonia 11.3.12 pulls Tmds.DBus.Protocol 0.21.2
  (GHSA-xrw6-gwf8-vvr9, Linux D-Bus only but restored regardless); pinned to
  0.21.3 via CentralPackageTransitivePinningEnabled. `dotnet list package
  --vulnerable --include-transitive` is clean; rerun it before each release.
- **Build inputs pinned 2026-09-02**: `global.json` locks the .NET SDK,
  `.config/dotnet-tools.json` locks vpk to the Velopack library version, and
  committed per-project lock files cover normal and NativeAOT win-x64 NuGet
  graphs with content hashes. GitHub Actions are commit-SHA pinned. Release and
  CI restores run in locked mode. The release script also rejects Visual
  Studio, MSVC, link.exe, or Windows SDK versions that differ from the committed
  build-environment manifest. That includes the Windows servicing build and
  Windows Installer engine used to rewrite the MSI compound stream.
- **Reproducible installer shipped 2026-09-02**: deterministic managed builds,
  fixed staging metadata, version-derived MSI identities, normalized WiX/CAB
  metadata, and normalized native-linker timestamps make repeated unsigned
  builds byte-identical. The public comparison tool validates Authenticode,
  removes only a valid terminal certificate table, and compares SHA-256 with a
  local build. Outer-only Authenticode keeps that comparison possible; the
  offline GPG manifest authenticates the inner MSI and update packages. The
  generated Claude, Codex, and Antigravity guides carry the complete clean-clone
  reproduction procedure so any agent can execute it consistently. The public
  README explains the trust model and gives users the direct verification
  commands.
- **PUBLISHED 2026-09-01** at `github.com/No-More-Secrets/thisismypc`;
  `AppConstants.UpdateUrl` points there.

## Binary hardening: DONE 2026-08-31

Full item-by-item record: `docs/release/hardening-checklist.md`. CFG/DEP/ASLR/CET/
EHCONT verified on both CoreCLR and AOT exes, IPC boundary audited against tm1,
SetDefaultDllDirectories at both entry points, WinVerifyTrust gate on winget.
All built, tested (ChildProcessGateTests incl. a live Integration case), and
committed; ultra review over the batch (044a4ad..f1d3739) returned zero findings
on 2026-09-01.

## AV / SmartScreen readiness (release-readiness chunk, pre-first-release)

The flag risk is Defender PUA heuristics on the app's behavior (elevated +
SYSTEM service + policy writes + DiagTrack disable), not the cert. Standing
mitigations already in place: never write Defender hives (deep-research rule),
everything reversible and user-confirmed. Pre-release process items:
- Submit each release to Microsoft's false-positive/developer portal BEFORE
  publishing; submission contact is inquiries@no-more-secrets.com.
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

`ChangeHistoryServiceTests.RevertChangeAsync_SwapsValuesAndCallsRevertFunc`
failed once in the full CI-safe run on 2026-09-02 and passed alone straight
after; not touched by that day's changes. Watch for a repeat.

## Autoruns page: SHIPPED 2026-09-02 (Startup & Services is now this one page)

Sam's read of Autoruns: every location is a key whose values or subkeys are
the items, and "disable" parks the item in an `AutorunsDisabled` sibling.
The module now does the same (docs/research/startup-scanner-rationale.md has
the location table and the toggler's rules). `AutorunsScanner` lists Logon,
Explorer, Internet Explorer, Scheduled Tasks, Services, Drivers, Font
Drivers, 32-Bit Drivers, Known DLLs, Winlogon, Winsock Providers, Print
Monitors, and Office; `AutorunToggler` moves values, keys, and Startup files,
flips tasks, and swaps a service or driver `Start` with an `AutorunsDisabled`
DWORD; every toggle is a pending change (`ChangeValueType.Autorun_State`)
with undo as the reverse move. `IRegistryService` gained typed
`ReadValue`/`WriteValue` and `CreateKey` (default implementations, so the
fakes did not change); `IStartupFolderService` gained `EnumerateDisabled`
and `Move`. The page is laid out like Autoruns: a tab per category (the strip
wraps to two rows), "Hide Microsoft entries" above it, and a search box
that swaps the tabs for one list across every category while it has text.
Row lists are ListBoxes, so only the visible rows are built. Rows
look like Autoruns' columns (icon, name, description, "(Verified)" signer,
path, file date, location headers with the key's write time, yellow for a
missing file, red for no verified signer); icons and Authenticode results
(embedded or catalog) load in the background; "Hide Windows entries" is on
by default like Autoruns, which is why Autoruns' Explorer, tasks, and
services tabs look shorter. Re-registered
copies (a live item beside its parked twin, which Autoruns lists twice and
refuses to touch) collapse into one flagged row; switching it off purges
the copy with its snapshot in the descriptor, so undo restores it. The old Startup, Scheduled Tasks, and Services tabs were
deleted the same day (Sam: never liked them); their scanners, change
factories, and the set inspector stay because Clean Boot sets and the Home
monitoring section apply through them.

Fresh-context review fixes, same day: moves refuse to overwrite a parked
twin; a service already `Start`=4 without the marker refuses both ways
instead of recording a change with no reverse; parked rows carry a `|parked`
setting-id suffix; `|` in a value or key name round-trips; shell handlers the
Context Menus page switched off (dash CLSID, Blocked list) show off with a
greyed switch; non-string values are not items;
`tools/check-binary-hardening.ps1` is now tracked, binds `-Path`
positionally, and skips non-PE files in folder mode.

Power plan creation, same day (Sam: "Can we add power plan creation?"):
New plan on the Power Plans page opens an inline form (name, copy of which
plan). Create stages a reversible change (`create-plan:{name}`, Category
Create); apply duplicates the source through powrprof and names the copy
with the marker description "Created by ThisIsMyPC", undo deletes that copy
by name and marker (never a same-named plan someone else made, never the
active plan). The staged plan shows as a green pending card under the list
with Remove; after Apply the page rescans and lists the real plan. Same
afternoon's page review: a queued deletion tints the card red with a red
"Queued" button and greyed buttons; the Apply badge counts actions; the
review panel wraps long lines and keeps the scrollbar lane; popups float
8px above the bar; the hibernation switch reads "Allow hibernation" (Sam
could not tell what on meant), and the Ultimate Performance switch is gone:
one "Add plan" dropdown (Sam: a toggle was the wrong control, and a
generic add button should put back Balanced, Power saver, and High
performance when they were deleted) lists the custom copy, then every
Windows plan missing from the list. A deleted stock plan comes back under
its own GUID from Windows' default store
(`PowerRestoreIndividualDefaultPowerScheme`, `add-stock-plan:{guid}`;
`PowerDuplicateScheme` with the destination GUID supplied was the first
attempt and fails with "no power plan with that GUID is registered", it only
reads plans that still exist), Ultimate Performance as the marked copy it
always was (its hidden source is e9a42b02-d5df-448d-aa00-03f14749eb61; the
code carried a GUID that exists nowhere until 2026-09-02); both are
reversible creates. The plan list has an order (Sam asked; Windows
enumerates by GUID): active plan, then Balanced, Power saver, High
performance, then Ultimate Performance, then custom plans by name
(`PowerPlanOrder`); a plan added by Apply slots into that order.

UI chapter two, 2026-09-02 evening (Sam: settings are extensive, tabs and
cards need one definition, Startup & Services must be dense): a plan's
settings open as one tab per subgroup with counts in the headers
(`PowerSettingsShotTests`); the tab strip is defined once in
`Controls.axaml` (wrapping panel, 14px headers) so a view writes
`Header="..."` and nothing else; `ToggleCard` (Controls/ToggleCard.cs,
Styles/ToggleCardTheme.axaml) is the one card-with-a-switch (title, (i)
tooltip, detail line, content slot, pending and inactive classes) behind
the card renderer, both row templates, the Modern Standby card, and the
gallery; the headless test app reads its includes from App.axaml instead of
a hand-kept twin (a missing include had left every ToggleCard without a
template in the walkthrough). Startup & Services rows are one line each
with a check box for enabled, a hairline between rows, and no card chrome;
file paths and locations (headers and per-row keys) show only when the
"Show paths" and "Show locations" boxes are ticked; a queued flip tints the
row green or red and draws a 3px bar at the left, so it reads on a row
that is already red or yellow. A fresh-context review over the chapter
(ten findings, all applied): the plan list re-sorts from row state after
an applied switch or a restored plan; the ToggleCard title trims and its
content slot hides when every line is hidden; the dense row and pending
tints live in Controls.axaml only; the test app's include regex takes any
attribute order and fails loudly off a checkout; Show locations binds the
headers' visibility instead of rebuilding every tab; the power fake is one
shared class; the plan sort ranks by key once per plan. Then (Sam: the
check boxes were cut off and off-centre in the dense rows, and check boxes
and the tab strip were never polished like the other controls): both have
their own control themes on the palette now, `Styles/CheckBoxTheme.axaml`
(18px box, shared outline, Overlay fill, accent fill with a white check,
button hover and press, no reserved 32px row) and `Styles/TabItemTheme.axaml`
(14px secondary text, primary on hover and selection, 2px accent line,
30px band with the text at its top); Startup rows are 23px. The filter
row is a 2x2 grid of show boxes beside the search box (Paths, Locations,
Windows, Microsoft); Windows and Microsoft entries are two disjoint groups,
each hidden until its box is ticked (Sam flipped the Microsoft default on
purpose); tab headers show the count shown, not X of Y. Later (Sam): accent
buttons (Apply, Create, Migrate) and the pending-count badge wear the
AccentOutline rim like every other accent-filled control; Fluent's 6px rows
above and below the switch track are gone, so a toggle card is 33px with
title and switch both centred; the Explorer page is two tabs (Explorer,
Taskbar) under its search box and fits one screen; the tab strip is
bordered chips (Surface fill, shared outline, sidebar tint plus accent
outline when selected) that stack cleanly in the two-row Startup &
Services strip; then (Sam liked them) the strip became a sunken well
(`SunkenBrush`, a shade above the window navy, the shared outline) as wide
as the search box above it, with the chips in justified rows
(`JustifiedWrapPanel`: each row stretches to the well's width, every chip
in a row growing by the same amount), and the apply-bar buttons share one
36px height whether or not Apply carries the count badge. Switching the active plan failed on
Sam's PC with Win32 error 1260: winutil had written the Group Policy value
`HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\ActivePowerScheme`
to pin its plan, and Windows refuses every other switch while it points
elsewhere, and (verified on Sam's PC) while the value exists at all, even
naming the target. The active-plan change now lifts the pin, calls
`PowerSetActiveScheme`, and puts the pin back naming the new plan (the old
one back when activation fails); undo hands the module the swapped
descriptor, so the same dance runs in reverse. 1260 maps to an AccessDenied
message that names the policy value. Later that
day (Sam: adding plans did nothing): a row of the Add plan dropdown closed
the menu before its command ran, and closing detached the menu content and
its bindings, so nothing was staged; the close is now posted after the
command, and the harness test clicks the row with real mouse events instead
of a raised event. Dropdowns (Button flyouts and ComboBox lists) sat on
Fluent's neutral gray; both now use the palette (Overlay tier dark, Surface
light, shared outline, hover and press like buttons, sidebar tint on the
chosen row, 6px rounding). The Add plan dropdown lost its "Windows plans
not in the list" header, and pending cards say what the plan is in a few
words (Sam: no apply/undo instructions in cards). Plan settings never
loaded (Sam: "stuck"): on Windows 11 26200 `PowerIsSettingRangeDefined`
answers false for range settings such as "Turn off hard disk after", and
for those `PowerReadPossibleFriendlyName` returns the setting's own name at
every index, so the possible-values walk never ended. A readable Min and
Max now marks a range (enumerated settings answer ERROR_FILE_NOT_FOUND to
both), the walk is capped at 64 and stops on a repeated label, and
`PowerPlanSettingsLiveTests` (Diagnostic) loads the active plan's settings
against a 30 s limit. Still owed from Sam: an elevated pass that applies
one stock plan restore, which has never run on Windows.

Scheduled tasks, same day (Sam: still an absurd number of tasks, and the
task rows had no icons): the scheduler reader now returns each task's
Exec action (command and arguments) or ComHandler class id, and the
Autoruns scanner resolves that to a file the way it does for Run values and
shell extensions (quotes, `%vars%`, bare names through System32 and PATH,
CLSID through InprocServer32). With a file, a task gets an icon, a signer,
a date, and the Windows filter; the live tab on this machine went from
every task to 45 of 223 with the default filter. Every task sits under one
"Task Scheduler" header instead of a header per task path. Windows' own
tasks whose author and description are string resources
(`$(@%SystemRoot%\System32\x.dll,-103)`) read through SHLoadIndirectString.
A task under `\Microsoft\Windows\` whose handler has no server path
(WaaSMedic, StartComponentCleanup) counts as a Windows entry. A task's
publisher is its program's company; the task author stands in only when
there is no program, so a user-created task no longer shows the user name
as its publisher. Long descriptions trim instead of running into the
signer column. Two Integration tests cover the real shell icon and the
catalog signer; a Diagnostic UI test boots the real main window, waits for
icons and signers, and dumps the visible tasks beside its screenshots. Later (Sam: the FanControl task reads "File not found: System32" while its Start In folder holds the exe): the reader now carries the Exec action's WorkingDirectory, and a bare Command resolves against it before the scheduler's System32, Windows, PATH walk.

Open: a real elevated pass on this machine (disable a Run entry and a
context-menu handler, apply, confirm in Autoruns, undo). Set entries
(`autorun:` setting ids) are not resolved by `StartupSetEntryInspector`; the
Home monitoring section still stages through the old `startup-entry:`,
`service-starttype:`, and `scheduled-task:` ids, which no page shows any
more. `TaskClassificationOverrideStore` and `ScheduledTaskClassifier` have
no UI now; delete or resurface.

## Deliberately NOT in this pass (new-module territory)

- Telemetry level / DiagTrack / error reporting → the planned Privacy & Telemetry
  module (already referenced as "arrives later" in Annoyances descriptions)
- Defender, SmartScreen, DoH/DNS, LSA → security module (mind the deep-research
  warning: never write Defender policy hives)
- Install/uninstall engine v1 SHIPPED 2026-08-30 (Modules.Software: winget
  catalog + inbox-app removal). Not yet verified in the running elevated app:
  sidebar entry, catalog staging, review panel actions section, a real install.
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
