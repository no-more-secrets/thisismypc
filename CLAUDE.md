# CLAUDE.md

Operating guidance for Claude Code in this repository. **How to DO things only** — rationale
lives in `_bmad-output/planning-artifacts/`, status in
`_bmad-output/implementation-artifacts/sprint-status.yaml`. Never append history here;
replace stale text.

## What this is

**ThisIsMyPC** — Windows system-control app (Armoury Crate / Autoruns / ShellExView /
ExplorerPatcher / HWiNFO consolidated). C# / .NET (`net10.0-windows10.0.22621.0`),
Avalonia 11 + CommunityToolkit.Mvvm, xUnit. Runs elevated (`app.manifest` requireAdministrator).

## The driving goal: mass debloat/tweak module

Sam is helping a friend debloat an the laptop. The near-term mission is to make this app
able to apply **tweak sets** so Claude Code on that machine can run the debloat safely,
reversibly, with visible before-states.

- **The tweak inventory** (audited from Sam's machine, the source data for the built-in
  sets): `C:\Users\user\Dev-Projects\laptop-debloat\TWEAKS.md`. Its ⚠️ items
  (Defender/WU/SmartScreen off) are opt-in only; its pitfalls section (WbioSrvc + biometric
  sensor freeze, HP firmware-update channels) must be encoded as `SettingEnforcement`
  companions and warnings, not prose.
- **Spec**: Epic 8 (Tweak Sets & Optimization Packs) + Epic 26 (enforcement) in
  `_bmad-output/planning-artifacts/epics.md`.
- **Sequence**: 26-2 executor delegation (done) → 26-3 SKU gating → service-control (SCM)
  interop + AppX removal capability (new, nothing exists — `ChangeValueType.Service_StartType`
  has no implementation behind it) → 26-4 enforcement metadata → 8.1 `ISetProvider` + set
  JSON schema → author the sets from TWEAKS.md → 8.2/8.3 preview & conflict UI.
- ⛔ Never shell out to O&O ShutUp or similar binaries — port their registry recipes into
  set definitions. Opaque tools break the before-state/undo guarantees.

## How work runs here

- **Commit straight to `main`.** No feature branches or PR ceremony.
- **BMAD artifacts are specs only** (PRD, architecture, epics, stories in `_bmad-output/`).
  Skip the agent role-play and "new context window" instructions.
- Story cycle: implement from the story file → full test suite → fresh-context code review
  → commit → update story `Status:` + sprint-status.yaml. Don't ask "proceed?" between
  stories. Stop only for irreversible things and naming/branding (Sam's).
- Read the story's Dev Notes before coding — they are usually near-code-complete and cite
  architecture.md line numbers.

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
  scanning). `Modules.Shell` is the reference implementation; `Modules.Startup` and
  `Modules.Power` are stubs.
