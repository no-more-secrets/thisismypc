# ThisIsMyPC -- Agentic Coding Tooling Reference

Living document of Claude Code skills, agents, MCP servers, and practices for this codebase.

---

## Skills (Installed)

Skills are invoked via `/skill-name` in Claude Code. They inject domain-specific guidance into the agent's context for the duration of a task.

Source: `~/.claude/skills/` (symlinked from `~/.agents/skills/`)

### .NET / C#

| Skill | Trigger | Purpose |
|---|---|---|
| `dotnet-best-practices` | General .NET code review | Broad quality check against solution/project conventions. |
| `dotnet-csharp-modern-patterns` | Writing C# 12-15 code | Records, pattern matching, primary constructors, collection expressions. |
| `dotnet-csharp-async-patterns` | Writing async/await code | Task patterns, ConfigureAwait, cancellation, common pitfalls. |
| `dotnet-csharp-dependency-injection` | Registering/resolving services | MS DI, keyed services, scopes, decoration, hosted services. |
| `dotnet-csharp-source-generators` | Working with source generators | IIncrementalGenerator, GeneratedRegex, LoggerMessage, CsWin32, STJ. |
| `dotnet-csharp-configuration` | Settings/config code | Options pattern, user secrets, feature flags, IOptions<T>. |
| `dotnet-domain-modeling` | Modeling domain types | Aggregates, value objects, domain events, rich models. |
| `dotnet-solid-principles` | Designing/refactoring classes | SOLID, DRY, SRP with C# anti-patterns and fixes. |
| `dotnet-design-pattern-review` | Reviewing pattern implementation | Validates design pattern usage, suggests improvements. |
| `dotnet-solution-navigation` | Orienting in the solution | Entry points, .sln/.slnx files, dependency graphs. |

### Interop / Performance / Deployment

| Skill | Trigger | Purpose |
|---|---|---|
| `dotnet-native-interop` | P/Invoke, COM interop | LibraryImport, marshalling, cross-platform resolution. |
| `dotnet-native-aot` | NativeAOT publishing | PublishAot, ILLink descriptors, P/Invoke, size optimization. |
| `dotnet-trimming` | Making code trim-safe | Annotations, ILLink descriptors, IL2xxx warnings. |
| `dotnet-performance-patterns` | Optimizing allocations/throughput | Span, ArrayPool, ref struct, sealed, stackalloc. |
| `dotnet-file-io` | File operations | FileStream, RandomAccess, FileSystemWatcher, MemoryMappedFile. |

### Utility

| Skill | Trigger | Purpose |
|---|---|---|
| `find-skills` | Discovering new skills | Helps find and install skills for unfamiliar domains. |
| `humanizer` | Editing prose/text | Removes signs of AI-generated writing. |

---

## MCP Servers

MCP servers extend Claude Code with live tool access to external systems.

### context7

User-scope plugin. Pulls current Avalonia and .NET reference docs; use it before answering API questions from memory.

### Docker MCP (`MCP_DOCKER`)

**Status:** Globally disabled for this project.

---

## Plugins (Claude Code Official)

User-scope plugins: `context7` (live library docs; keep it, it pulls current Avalonia and .NET references). `neon` is installed at user scope for other projects and disabled for this repo in `.claude/settings.json`.

Removed 2026-09-01: `ralph-loop`, `frontend-design` (project plugins, no fit for an Avalonia desktop app), the 54 BMAD skills under `.claude/skills/`, and the GSD framework (user-scope agents and commands). BMAD is closed; its planning history stays in `_bmad-output/`. Backups of the user-scope removals: `~/.claude/backups/trim-2026-09-01/`.

---

## Subagent Types (Built-in)

Claude Code's built-in `Agent` tool supports specialized subagent types:

| Type | Use Case |
|---|---|
| `Explore` | Fast codebase exploration -- file patterns, keyword search, architecture questions |
| `Plan` | Software architect -- implementation strategy, step-by-step plans, trade-offs |
| `general-purpose` | Complex multi-step tasks, research, code changes |

---

## IDE Setup: Visual Studio 2026

Primary IDE for building, debugging, XAML preview, and NativeAOT diagnostics. VS Code + Claude Code remains the agentic workflow environment.

### Recommended Extensions

| Extension | Purpose |
|---|---|
| **Avalonia for Visual Studio** | XAML previewer, IntelliSense for .axaml, control templates |
| **CsWin32** | Source generator support for NativeMethods.txt editing |
| **EditorConfig Language Service** | Syntax highlighting/validation for .editorconfig naming rules |
| **SQLite/SQL Server Compact Toolbox** | Inspect history.db during development |
| **Markdown Editor** | Edit docs without leaving VS |
| **Roslynator** | 500+ analyzers/refactorings on top of AnalysisLevel=latest-all |
| **SonarLint** | Security issue detection (important for admin-level system tool) |
| **Fine Code Coverage** | Inline test coverage visualization across the 14 test projects |
| **ILLink Analyzer** | Surfaces trimming/NativeAOT warnings in editor before publish (if available) |

**Skip:** ReSharper (heavy; Roslynator + built-in analyzers covers it), GitHub Copilot (using Claude Code).

---

## Best Practices for This Project

### When to invoke skills

- **Before writing any Avalonia UI code:** read `docs/avalonia-guide.md`. The app uses CommunityToolkit.Mvvm and compiled bindings; there is no Zafiro or ReactiveUI in the solution
- **Before P/Invoke or COM work:** `/dotnet-native-interop` + `/dotnet-native-aot` (NativeAOT compatibility)
- **Before performance-sensitive code:** `/dotnet-performance-patterns`
- **During code review:** `/dotnet-best-practices` + `/dotnet-design-pattern-review`
- **When modeling ChangeDescriptor/module contracts:** `/dotnet-domain-modeling`
- **When writing DI registration:** `/dotnet-csharp-dependency-injection`

### Skill stacking

Multiple skills can be active in one session. For a typical module implementation task, invoke:
1. `dotnet-csharp-modern-patterns` (C# 14 idioms)
2. `dotnet-native-aot` (NativeAOT safety check)
3. `dotnet-csharp-dependency-injection` (module registration)

### UI verification: the sight harness

`tests/ThisIsMyPC.App.UiTests` renders the real UI headlessly (Avalonia.Headless + Skia) and saves PNG screenshots to `artifacts/ui-shots/<suite>/`. Read the PNGs as images. Every XAML or ViewModel change is verified this way before commit, never by reasoning about XAML and never by asking for a manual launch.

```
dotnet test tests/ThisIsMyPC.App.UiTests --configuration Release --filter "Category!=Diagnostic"  # CI-safe view tests
dotnet test tests/ThisIsMyPC.App.UiTests --configuration Release --filter "Category=Diagnostic"   # full-app walkthrough
```

`UiSession` is the driver: `ForView` hosts one view on fake data, `ForMainWindow` boots the real window on the real service graph with test-safe swaps. Full rules, including the edge-geometry contract, are in `CLAUDE.md`.

### Agent parallelization

- Use `Explore` subagents for codebase questions that need >3 searches
- Use `general-purpose` agents in parallel for independent file edits
- Use background agents (`run_in_background: true`) for long research while continuing other work
