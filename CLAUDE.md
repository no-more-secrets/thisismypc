# CLAUDE.md

Operating guidance for Claude Code in this repository. **How to DO things only** — rationale
lives in `_bmad-output/planning-artifacts/`, current status and plans in
`docs/planning/` (`refinement-backlog.md` is the master list). Never append history here;
replace stale text.

## What this is

**ThisIsMyPC** — Windows system-control app (Armoury Crate / Autoruns / ShellExView /
ExplorerPatcher / HWiNFO consolidated). C# / .NET (`net10.0-windows10.0.22621.0`),
Avalonia 11 + CommunityToolkit.Mvvm, xUnit. Runs elevated (`app.manifest` requireAdministrator).

## Where the project is: BMAD is closed

Every defined BMAD story shipped (epics 1-10, 25-28 complete; 20-1 standalone).
Epics that never got stories (11-19, 20-remainder, 21-24) were retired and form
the backlog menu for what comes next. Do not run the BMAD workflow (`_bmad/`)
for new work.

**The current chapter** is Sam's post-BMAD plan, two tracks:
1. **Custom modules ported from open-source Windows utilities** — the retired epics
   (display/DDC-CI, system info, ASUS/ATKACPI, OpenRGB, drivers, network/firewall,
   profiles, privacy/telemetry, WU remainder, software install) are the menu, not a
   contract. ⛔ Never shell out to opaque binaries (O&O ShutUp etc.) — port their
   registry/API recipes into set definitions or modules; opaque tools break the
   before-state/undo guarantees.
2. **UI/UX overhaul** — light theme, toasts, card highlight/observable degradation,
   the actionable Owner Mode callout, save-form dedup, and everything tagged
   "deferred to the UI/UX chapter" in story files. Naming/branding decisions are Sam's.

`_bmad-output/planning-artifacts/` (PRD, architecture, epics) remains the reference
spec; `TWEAKS.md` there contains personal-machine details — move out before the repo
goes public.

## How work runs here

- **Commit straight to `main`.** No feature branches or PR ceremony.
- Work cycle: implement → full test suite → fresh-context code review for anything
  substantial → commit → update whatever tracking doc the current chapter uses.
  Don't ask "proceed?" between tasks. Stop only for irreversible things and
  naming/branding (Sam's).

## Build & test

```
dotnet build --configuration Release
dotnet test --filter "Category!=Integration&Category!=Diagnostic"   # what CI runs
```

- If `dotnet` builds fail with MSB4242 (workload manifest mismatch — has happened after a
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
change MUST be verified by looking at rendered screenshots before commit** —
never by reasoning about XAML, and never by asking Sam to launch the app.
Manual verification from Sam is reserved for things only the elevated exe can
show (real applies, tray, UAC).

```
dotnet test tests/ThisIsMyPC.App.UiTests --configuration Release --filter "Category!=Diagnostic"  # CI-safe view tests
dotnet test tests/ThisIsMyPC.App.UiTests --configuration Release --filter "Category=Diagnostic"   # full-app walkthrough, ~8s
```

- Screenshots land in `artifacts/ui-shots/<suite>/` (gitignored). Read the PNGs;
  the loop is edit → run → look → fix.
- `UiSession` is the driver. `ForView(view, vm, suite)` hosts one view with fake
  data (CI-safe). `ForMainWindow(suite)` boots the real MainWindow on the real
  service graph with test-safe swaps (fake winget/restore-point/updater, temp
  data paths) — scans read the live system, so those tests carry
  `Category=Diagnostic`.
- Interact like a person, not through commands: `ClickText("Windows Apps")`
  sends real mouse events; `Type(box, "text")` sends keystrokes;
  `WaitForAsync(...)` pumps while background scans finish;
  `DescribeVisibleText()` lists what's on screen when a find fails.
- Never click Apply in a `ForMainWindow` session against real modules — staging
  is read-only, applying writes to the live system. Apply-flow tests use fake
  modules or `ForView`.
- New views: add a screenshot test to the CI-safe suite, and the walkthrough
  picks up new sidebar modules automatically.

## Architecture must-rules (violations get caught in review)

Full rules: `_bmad-output/planning-artifacts/architecture.md` (esp. the "AI agent MUST" list).

- **Core is pure** — data types, interfaces, services logic. No Win32/COM/WMI calls
  (those live in `Interop.*` projects). Production `EnforcementExecutor` goes in
  `App/Services/`, not Core.
- **Every mutation goes through the pending-changes pipeline**: `ChangeDescriptor` (requires
  non-null `BeforeValue`) → `ChangeGroup` → `IPendingChangesService.Stage` → `ApplyAllAsync`
  with rollback. Changes are built by static factories (see
  `Modules.Shell/Changes/*ChangeFactory.cs`).
- **Enforcement routing**: `Enforcement != null` → `IEnforcementExecutor`; `null` → module
  delegate directly. No other heuristics; never route everything through the executor; the
  executor never calls modules directly (delegate pattern).
- **Modules**: implement `IModule` (`Core/Modules/IModule.cs`), registered explicitly in
  `App/App.axaml.cs` via `AddSingleton<IModule, X>()` (NativeAOT-safe — no reflection
  scanning). `Modules.Shell` is the reference implementation for custom-view modules;
  `Modules.Annoyances` is the reference card-renderer consumer.
- **Session 0 service** (`ThisIsMyPC.Service` + `ThisIsMyPC.Ipc.Contracts`): GPLv2 in
  this repo (docs/why-gplv2.md — there is no private repo). IPC is the hardened named
  pipe from 28-1; new message types extend the envelope, never change it.
