<!-- GENERATED from CLAUDE.md by tools/sync-agent-guides.ps1. Do not edit above the marker; edit CLAUDE.md and rerun the script. -->

# AGENTS.md

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
   release-key ceremony (docs/release/update-signing.md). Process: OV cert
   delivery (purchased 2026-09-01, token due about 2026-09-08), Defender
   false-positive submission, VirusTotal CI canary, winget distribution.
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
- New dependencies: version goes in `Directory.Packages.props`, versionless
  `PackageReference` in the csproj.
- Tests: one project per module mirroring `src/`; fakes in
  `tests/ThisIsMyPC.Core.Tests/Fakes/`; `Category=Integration|Diagnostic` traits for anything
  touching the live system (excluded from CI).
- The TIPC001 analyzer (`analyzers/`) auto-applies to every project.

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

### Edge-geometry contract (every module page, no exceptions)

The MainWindow host Border pads 24 left/top/bottom and 6 right. Every view
gives its scroll content a 16px right margin: the scrollbar overlays the
viewport (it reserves no space), so 16 leaves a ~10px gap between content and
the 4px thumb and puts bg-to-content at ~24 on both sides. Fixed chrome above
a scroller (card-page toolbar rows) uses right margin 16 too, so it shares the
content edge exactly. Inside a page: first element starts 12px below the host
padding; scrolled content ends 24px above it. Views never set their own left
margins. `Controls.axaml` zeroes TabControl/TabItem padding globally (stock
Fluent pads tab content 12px per side, which pushes scrollbars out of the lane
and indents content); a new tab page needs `Margin="0,12,0,0"` on its
TabControl and nothing else.

After touching any page layout, verify parity in pixels, not by eye and never
from XAML: screenshot the pages (walkthrough or `EdgeGeometryShotTests`), then
run `tools/measure-edge-geometry.ps1` over the PNGs. Every page must read
ContentL 25 and LaneFrom 10; ContentR 23 except width-capped pages (Home,
Settings, Display, Gallery cap content width, so their ContentR is large);
ContentT 37 give or take 4, except tab pages (~55: Fluent centers header text
in the tab min-height). Any page off those numbers is the bug, even if it
looks close.

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
- **Session 0 service** (`ThisIsMyPC.Service` + `ThisIsMyPC.Ipc.Contracts`): GPLv2 in
  this repo (docs/why-gplv2.md; one repo holds everything). IPC is the hardened named
  pipe from 28-1; new message types extend the envelope, never change it.
- **Installer** (`ThisIsMyPC.Installer`, docs/release/packaging.md): elevated Avalonia
  launcher around the Velopack MSI; references Core and Interop.Win32 only, links the
  App's Theme.axaml and fonts. Pages are sight-harness tested (`InstallerShotTests`).
  Anything it writes and then trusts goes under the hardened ProgramData folder,
  never %TEMP%.

<!-- vendor-specific: everything below this line is kept by tools/sync-agent-guides.ps1; everything above is generated from CLAUDE.md -->

## Codex notes

- Where the master says `/code-review`, run a fresh-context review with a new
  session or a review sub-task; the bar is the same: findings fixed before commit.
- Where the master says to read screenshots as images, open the PNGs under
  `artifacts/ui-shots/` with your image input; never verify UI from the XAML.
- The `gh` role check in "How work runs here" applies unchanged.
