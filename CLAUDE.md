# CLAUDE.md

Operating guidance for Claude Code in this repo. **How to DO things only.** Detailed plans
and status live in `docs/planning/` (`refinement-backlog.md` is the master list); design
rationale in `docs/` (index: `docs/README.md`). Never append history here; replace stale
text.

**Em dashes are banned everywhere.** This file, code comments, docs, commit messages,
UI strings, chat replies. Use periods, semicolons, colons, or commas; even a
grammatically loose hyphen beats an em dash. The rule is infectious: purge any em
dash you find in text you touch.

## What this is

**ThisIsMyPC** is a Windows system-control app that consolidates the trusted-utility
zoo (winutil / O&O ShutUp10 / Autoruns / ShellExView / UniGetUI territory) with
before-state capture and undo for everything. Post-release ambition: replace OEM
control bloatware (Armoury Crate, Vantage, Omen Hub) with clean hardware modules.
C# / .NET (`net10.0-windows10.0.22621.0`), Avalonia 11 + CommunityToolkit.Mvvm, xUnit.
Runs elevated (`app.manifest` requireAdministrator). **The app corresponds to the PC,
not a user profile**; machine-scoped decisions win (one install, one database,
all-profiles coverage).

## Roadmap

Full detail, ordering rationale, and per-module deferred lists:
`docs/planning/refinement-backlog.md`.

Feature work is complete (all modules plus the UI/UX chapter; history in the
backlog).

1. **Release prep (current)**. Remaining hard blocker, Sam-gated: GPG
   release-key ceremony (docs/release/update-signing.md). SSL.com eSigner OV
   signing and signed-to-unsigned reproducibility passed end to end on
   2026-09-03. Then: Defender false-positive submission, VirusTotal CI canary,
   winget distribution.
2. **Post-release menu**: retired BMAD epics (ASUS/ATKACPI, OpenRGB, drivers,
   network/firewall, profiles, WU remainder) and install-engine leftovers
   (OneDrive/Edge removal, OEM tools, progress/cancel).

The BMAD planning framework that drove the first chapter was removed from the repo
on 2026-09-01; do not reinstall it. ⛔ Never shell out to opaque binaries
(O&O ShutUp etc.); port their registry/API recipes into set definitions or modules, or
the before-state/undo guarantees break.

## How work runs here

**Determine the role of this session once, at the start**, and follow the
matching path for the whole session:

```
gh repo view No-More-Secrets/thisismypc --json viewerPermission -q .viewerPermission
```

- **Owner session** (`ADMIN` or `WRITE`): you work for the repo owner (Sam). Commit
  straight to `main`; no feature branches, no PR. Push after every commit.
  Update `refinement-backlog.md` when a change closes or adds an item.
- **Contributor session** (`READ`, `NONE`, an error, or `origin` is a fork):
  you work for a contributor. Never commit to `main`. For anything beyond a
  small fix, open or pick an issue on `No-More-Secrets/thisismypc` first and
  get the approach agreed there. Branch from `main`, and finish with a PR
  against `No-More-Secrets/thisismypc` using the PR template. Mention the
  backlog item the PR touches instead of editing the backlog.

The cycle is the same in both roles: implement, full test suite, sight harness
screenshots for any UI change, fresh-context code review (`/code-review`) for
anything substantial, commit. Don't ask "proceed?" between tasks. Stop only for
irreversible things and naming/branding; those are the owner's decisions, and a
contributor session raises them in the issue.

**This file is the master copy.** `AGENTS.md` (Codex) and `GEMINI.md`
(Antigravity) are generated twins: the same body, plus a vendor-specific
appendix below a marker line. Edit only this file. The pre-commit hook in
`tools/git-hooks` regenerates the twins on any commit that touches one of the
three files; enable it once per clone with `tools/install-git-hooks.ps1`
(it sets `core.hooksPath`). Without the hook, run
`tools/sync-agent-guides.ps1` by hand; CI fails on a stale twin either way.
Vendor-specific notes go below the marker in the twin, never above it.

## Build & test

New clone: `.\Setup.ps1` once (prerequisite checks, git hooks, guide parity,
role, build, CI-safe tests). Then:

```
dotnet build --configuration Release
dotnet test --filter "Category!=Integration&Category!=Diagnostic"   # what CI runs
```

- If `dotnet` builds fail with MSB4242 (workload manifest mismatch; has happened after a
  half-finished VS servicing update): `$env:MSBuildEnableWorkloadResolver = "false"` per
  shell unblocks; this solution uses no workloads. A completed VS update fixes it properly.
- Errors come from the log, never from a screenshot. A Debug run opens a
  console window ("ThisIsMyPC logs (Debug)") and mirrors it into the Visual
  Studio Output window; both carry every status-bar line, every module
  apply/revert/action result with its category, message, exception, and
  elapsed time, and every refused powrprof call with its Win32 code. Release
  writes the same records as JSON to `%ProgramData%\ThisIsMyPC\logs`.
  Anything the user sees as an error is copyable from there.
- New dependencies: version goes in `Directory.Packages.props`, versionless
  `PackageReference` in the csproj.
- Avalonia's optional `Avalonia.BuildServices` telemetry is deliberately an
  assetless direct dependency in every project whose dependency graph reaches
  Avalonia. This suppresses its transitive task and collector at restore time.
  Keep the exclusion until Avalonia stops depending on it; an
  environment-variable opt-out is not an equivalent removal.
- Tests: one project per module mirroring `src/`; fakes in
  `tests/ThisIsMyPC.Core.Tests/Fakes/`; `Category=Integration|Diagnostic` traits for anything
  touching the live system (excluded from CI).
- The TIPC001 analyzer (`analyzers/`) auto-applies to every project.
- **All build and test output goes under one gitignored root, `artifacts/`,
  shaped `artifacts/<type>/<build>/`.** One folder per type, one per build
  within it. Types: `releases/<version>` (shippable outputs),
  `staging/<version>` (release build staging), `ui-shots/<suite>` (sight
  harness), `aot/<name>` (ad-hoc NativeAOT publishes), `diagnostics/<name>`
  (one-off logs, dumps, audits, probes). Build here, never in the scratchpad,
  the repo root, or a project folder; a throwaway AOT publish goes to
  `artifacts/aot/<name>/`, not the scratchpad. `builds/` is retired.
- Output piles up (a full reproducible-build run is gigabytes).
  `tools/clean-build-output.ps1` empties `artifacts/` (add `-IncludeBinObj` to
  also clear every `bin`/`obj`, `-WhatIf` to preview). It never touches tracked
  files, and every path it removes is rebuilt on demand.

## Reproduce a released signed installer

Use this procedure whenever someone asks whether an official signed installer
was built from its tagged source. Never run the downloaded exe. Never modify it
in place, and never use `-AllowUnsignedRelease` for a public-release check.

1. Start from a fresh clone so local or concurrent edits cannot contaminate the
   result. Read the version from the release filename and check out that exact
   tag. Do not switch a dirty working tree.
2. Install the exact machine-level inputs listed in
   `tools/reproducible-build-environment.json`. Do not edit the manifest to
   match the machine. The validator intentionally stops on any difference. Use
   a disposable Windows VM matching the manifest when the host cannot match.
3. Run the one-command verifier. Official releases are NativeAOT only. The
   verifier recognizes the package shape, clones the exact tag into a
   disposable directory, builds it, and compares it:

```
$releasedInstaller = 'C:\path\to\ThisIsMyPC-Installer-1.0.0.exe'
.\tools\verify-release.ps1 -ReleasedInstaller $releasedInstaller
```

Success is exit code 0 together with `Reproducible release verified` and one
SHA-256 value. Any failed trust, environment, structure, or content check exits
nonzero. The comparison requires valid Authenticode on the outer installer,
embedded MSI,
app, service, and Velopack helpers. It compares MSI metadata and every installed
file after removing only structurally valid certificate tables from temporary
copies. The download is unchanged. A mismatch is not expected signing noise.
Report the named differing path and hashes. Full design and lower-level commands
live in `docs/release/packaging.md`.

## Sign a release with SSL.com eSigner

Use CKA 1.1.2 in Automated Code Signing and Production mode. Keep SSL.com's
malware blocker enabled. Download the unmodified CodeSignTool 1.3.3 Windows zip
from SSL.com; do not run from a previously extracted directory. The release
scripts hash-check the archive and the installed CKA runtime against
`tools/esigner-signing-environment.json` before building.

```powershell
$env:ESIGNER_USERNAME = 'SSL.com account username'
$env:ESIGNER_CREDENTIAL_ID = 'code-signing credential ID, not eSeal ID'
$env:ESIGNER_CODESIGNTOOL_ARCHIVE = 'C:\path\to\CodeSignTool-v1.3.3-windows.zip'
.\tools\build-release.ps1 -Version 1.0.0 `
  -Aot `
  -SignThumbprint '40-character certificate thumbprint'
```

For a local release, enter the account password only at the script's secure
prompt. For CI, supply `ESIGNER_PASSWORD` only through the runner secret store
and only to the signing step. Never print or commit the password, TOTP seed, or
CKA master key. CodeSignTool passes the password to its Java process because the
vendor offers no protected input channel, so release runners must not host
untrusted same-user processes.

CI builds unsigned without `ESIGNER_PASSWORD`, then starts a separate Windows
signing step with the secret and calls `tools/sign-release-installer.ps1` over
the completed release directory. That script removes the password from its
environment before any child starts. `build-release.ps1` refuses to run when
the password is present, so MSBuild, NuGet, and vpk cannot inherit it.

The script requires the No More Secrets, LLC identity. Velopack sends each exact
installed first-party executable through the pinned malware scanner and
SignTool before packing. The MSI and outer installer follow the same scan and
sign gate. Every signature needs an RFC 3161 timestamp, and the final canonical
tree must match the preserved unsigned build. Do not bypass these gates. If a
vendor tool changes, investigate it and deliberately update the committed pins;
never edit the manifest merely to match the machine.

## UI work: use the sight harness, not Sam's eyes

`tests/ThisIsMyPC.App.UiTests` renders the real UI headlessly (Avalonia.Headless
+ Skia) and saves PNG screenshots you can Read as images. **Any XAML/view-model
change MUST be verified by looking at rendered screenshots before commit**,
never by reasoning about XAML, and never by asking Sam to launch the app.
Manual verification from Sam is reserved for things only the elevated exe can
show (real applies, tray, UAC). **Write every manual test for a common Windows
user**, whoever is asked to run it: clicks, menus, what the screen shows. No
commands, no paths, no log files, no developer knowledge assumed. The app
exists for people who do not know what a command prompt is; a test written
for the developer is not a test of the product.

```
dotnet test tests/ThisIsMyPC.App.UiTests --configuration Release --filter "Category!=Diagnostic"  # CI-safe view tests
dotnet test tests/ThisIsMyPC.App.UiTests --configuration Release --filter "Category=Diagnostic"   # full-app walkthrough, ~8s
```

- Screenshots land in `artifacts/ui-shots/<suite>/` (gitignored). Read the PNGs;
  the loop is edit → run → look → fix.
- `UiSession` is the driver. `ForView(view, vm, suite)` hosts one view with fake
  data (CI-safe). `ForMainWindow(suite)` boots the real MainWindow on the real
  service graph with test-safe swaps (fake winget/restore-point/updater, temp
  data paths); scans read the live system, so those tests carry
  `Category=Diagnostic`.
- Interact like a person, not through commands: `ClickText("Windows Apps")`
  sends real mouse events; `Type(box, "text")` sends keystrokes;
  `WaitForAsync(...)` pumps while background scans finish;
  `DescribeVisibleText()` lists what's on screen when a find fails.
- Never click Apply in a `ForMainWindow` session against real modules: staging
  is read-only, applying writes to the live system. Apply-flow tests use fake
  modules or `ForView`.
- New views: add a screenshot test to the CI-safe suite, and the walkthrough
  picks up new sidebar modules automatically.
- The harness evolves with the UI/UX chapter: as the visual feedback and
  interaction loop improves, update this section to match.

### Shared region review (Debug builds)

Ctrl+Shift+A toggles frozen annotation and live navigation. Each drag adds a figure
with a stable number across pages. Badge clicks select figures; pencil clicks edit notes;
Delete removes the selected figure. Escape cancels a note editor or suspends review.
Ctrl+Shift+Alt+A clears the whole session. Switching pages does not clear figures.
Saved views are keyed by page route, client dimensions, and display scale. New sizes
capture the live layout; returning to an earlier size restores its visible figures.
Read captures[].logicalWidth/logicalHeight and renderScale for dimension-specific reports.
Read tools/read-region-selection.ps1 and check active. Schema 3 figures include
pageRoute, captureId, imagePath, bounds, and notes. Group by captureId and inspect
each image. A suspended review still contains valid figures. Never use a stale
process record or assume earlier figures refer to the current live page.
See docs/region-review.md for controls, route identifiers, and prototype limits.

### UI review checklist (learned the hard way, 2026-09-02)

Run over every page before showing it; each line cost a correction once.

- Vertical space is tight: 12px host top padding, 4px page margin, 30px tab
  band with text at the top. When a gap reads as spare room, fix the whole
  stack and measure every page, not the one that was pointed at.
- A staged state is loud: tint the card (green create, red delete), colour
  the button, grey out sibling buttons. Counters count everything staged,
  one-way actions included.
- No text trails off an edge: wrap or trim every line in cards and popups;
  popups keep the 16px scrollbar lane and sit 8px clear of adjacent chrome.
- Toggles only for state the person owns. "Add X" is a button or a
  dropdown entry. A switch label says what on means as a verb ("Allow
  hibernation"); an (i) tooltip does not replace a clear label.
- New items appear at the top, in edit mode, never appended.
- One outline per row: inner text boxes and buttons are borderless; header
  rows, search boxes, and cards share the same left and right edges.
- Big lists get a tab per category, no Everything tab, search that replaces
  the tabs, virtualized rows.
- Descriptions are terse product copy, not instructions.
- Before answering "not fixed", compare the running exe's build time with
  the last commit: Visual Studio runs the last successful build when the
  app was still running and locked the output.

### Edge-geometry contract (every module page, no exceptions)

The MainWindow host Border pads 24 left and bottom, 12 top, and 6 right. Every view
gives its scroll content a 16px right margin: the scrollbar overlays the
viewport (it reserves no space), so 16 leaves a ~10px gap between content and
the 4px thumb and puts bg-to-content at ~24 on both sides. Fixed chrome above
a scroller (card-page toolbar rows) uses right margin 16 too, so it shares the
content edge exactly. Inside a page: first element starts 4px below the host
padding (24 host plus 12 page read as spare room on every page; Sam,
2026-09-02, twice); scrolled content ends 24px above it. Views never set their own left
margins. `Styles/TabStripTheme.axaml` owns tab strips: the headers are chips in a
sunken well that ends 16px short of the page edge (the same edge a search
box above it uses) while the selected content keeps the full width for its
scrollbar lane; a new tab page needs `Margin="0,4,0,0"` on its TabControl
and nothing else; a panel that holds a TabControl carries no right margin of
its own, each fixed row above the strip keeps 16 (see PowerView's settings
panel).

After touching any page layout, verify parity in pixels, not by eye and never
from XAML: screenshot the pages (walkthrough or `EdgeGeometryShotTests`), then
run `tools/measure-edge-geometry.ps1` over the PNGs. Every page must read
ContentL 25 and LaneFrom 10; ContentR 23 except width-capped pages (Home,
Settings, Display, Gallery cap content width, so their ContentR is large);
ContentT 17 give or take 4, tab pages included (the strip's well starts
where any other first element does). Any page off those numbers is the bug,
even if it looks close.

## Architecture must-rules (violations get caught in review)

- **Core is pure**: data types, interfaces, services logic. No Win32/COM/WMI calls
  (those live in `Interop.*` projects). Production `EnforcementExecutor` goes in
  `App/Services/`, not Core. COM under NativeAOT: `LibraryImport` CoCreateInstance
  plus hand-rolled vtables (see `Interop.Com`); no `ComWrappers`, no WMI.
- **Every reversible mutation goes through the pending-changes pipeline**: `ChangeDescriptor`
  (requires non-null `BeforeValue`) → `ChangeGroup` → `IPendingChangesService.Stage` →
  `ApplyAllAsync` with rollback. Changes are built by static factories (see
  `Modules.Shell/Changes/*ChangeFactory.cs`).
- **One-way operations** (installs, uninstalls, upgrades) go through
  `IPendingActionsService` instead; no fabricated before-states, no history,
  continue-on-failure. Reversible = change, irreversible = action; a module needing
  actions implements `IActionModule`. `Modules.Software` is the reference.
- **Enforcement routing**: `Enforcement != null` → `IEnforcementExecutor`; `null` → module
  delegate directly. No other heuristics; never route everything through the executor; the
  executor never calls modules directly (delegate pattern).
- **Modules**: implement `IModule` (`Core/Modules/IModule.cs`), registered explicitly in
  `App/App.axaml.cs` via `AddSingleton<IModule, X>()` (NativeAOT-safe, no reflection
  scanning). `Modules.Shell` is the reference implementation for custom-view modules;
  `Modules.Annoyances` is the reference card-renderer consumer.
- **Session 0 service** (`ThisIsMyPC.Service` + `ThisIsMyPC.Ipc.Contracts`): GPLv3 in
  this repo (docs/why-gplv3.md; one repo holds everything). IPC is the hardened named
  pipe from 28-1; new message types extend the envelope, never change it.
- **Installer** (`ThisIsMyPC.Installer`, docs/release/packaging.md): elevated Avalonia
  launcher around the Velopack MSI; references Core and Interop.Win32 only, links the
  App's Theme.axaml and fonts. Pages are sight-harness tested (`InstallerShotTests`).
  Anything it writes and then trusts goes under the hardened ProgramData folder,
  never %TEMP%.
