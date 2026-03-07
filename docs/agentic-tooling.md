# ThisIsMyPC -- Agentic Coding Tooling Reference

Living document of Claude Code skills, agents, MCP servers, and practices for this codebase.

---

## Skills (Installed)

Skills are invoked via `/skill-name` in Claude Code. They inject domain-specific guidance into the agent's context for the duration of a task.

Source: `~/.claude/skills/` (symlinked from `~/.agents/skills/`)

### Avalonia / UI

| Skill | Trigger | Purpose |
|---|---|---|
| `avalonia-zafiro-development` | Writing any Avalonia code | **Mandatory.** Core conventions, Zafiro toolkit rules, functional-reactive MVVM, cross-platform patterns. |
| `avalonia-layout-zafiro` | Building UI layouts | Shared styles, layout patterns, Zafiro component usage. |
| `avalonia-viewmodels-zafiro` | Creating ViewModels or wizards | ViewModel lifecycle, ReactiveUI patterns, wizard flow design. |

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

### Avalonia DevTools (`avalonia_devtools`)

**Config:** Global (`~/.claude.json`)
```json
{
  "avalonia_devtools": {
    "type": "stdio",
    "command": "avdt",
    "args": ["mcp"]
  }
}
```

**Tools provided:**
- `attach-to-file` -- Inspect/preview a specific XAML file without running the app
- `attach-to-app` -- Connect to a running Avalonia application
- `tree` / `search` -- Navigate the visual tree, find nodes
- `props` / `set-prop` -- Read/write control properties
- `styles` / `resources` -- Inspect styles and resources
- `screenshot` -- Capture UI screenshots
- `action` / `input` / `pseudo-class` -- Simulate interactions

**Usage rules:**
- Use `attach-to-file` when inspecting/designing XAML in isolation
- Use `attach-to-app` when debugging a running application
- Node IDs from `tree`/`search` are internal -- never show to user
- After XAML changes, invalidate node IDs by calling `tree`/`search` again
- Project must reference `AvaloniaUI.DiagnosticsSupport` package

### Docker MCP (`MCP_DOCKER`)

**Status:** Globally disabled for this project.

---

## Plugins (Claude Code Official)

Enabled in `.claude/settings.json`:

| Plugin | Purpose |
|---|---|
| `ralph-loop` | Autonomous coding loop with self-review cycles. `/ralph-loop:ralph-loop` to start, `/ralph-loop:cancel-ralph` to stop. |
| `frontend-design` | Production-grade frontend interface design. `/frontend-design:frontend-design` to invoke. |

---

## BMAD Agents

BMAD (Build Manage Architect Deploy) is an agent framework installed at `_bmad/`. It provides specialized agents for project lifecycle tasks. Key agents used so far:

| Agent | Invocation | Purpose |
|---|---|---|
| Product brief | `/bmad-bmm-create-product-brief` | Collaborative product brief creation |
| PRD | `/bmad-bmm-create-prd` | Full PRD generation |
| UX Design | `/bmad-bmm-create-ux-design` | UX specification and design patterns |
| Architecture | `/bmad-bmm-create-architecture` | Architecture solution design |
| Technical research | `/bmad-bmm-technical-research` | Domain/technology research |
| Brainstorming | `/bmad-brainstorming` | Feature ideation sessions |
| Code review | `/bmad-bmm-code-review` | Adversarial code review |
| Story creation | `/bmad-bmm-create-story` | Implementation story files |
| Dev story | `/bmad-bmm-dev-story` | Execute story implementation |

Full list: run `/bmad-help` for routing guidance.

---

## GSD (Get Stuff Done)

Project management and execution framework at `~/.claude/commands/gsd/`. Provides milestone tracking, phase planning, parallel execution, and debugging workflows.

Key commands: `/gsd:progress`, `/gsd:plan-phase`, `/gsd:execute-phase`, `/gsd:debug`

---

## Subagent Types (Built-in)

Claude Code's built-in `Agent` tool supports specialized subagent types:

| Type | Use Case |
|---|---|
| `Explore` | Fast codebase exploration -- file patterns, keyword search, architecture questions |
| `Plan` | Software architect -- implementation strategy, step-by-step plans, trade-offs |
| `general-purpose` | Complex multi-step tasks, research, code changes |
| `gsd-executor` | GSD plan execution with atomic commits |
| `gsd-phase-researcher` | Research before planning a phase |
| `gsd-planner` | Phase plan creation |
| `gsd-debugger` | Scientific debugging with persistent state |
| `gsd-verifier` | Goal-backward verification of phase completion |

---

## IDE Setup — Visual Studio 2026

Primary IDE for building, debugging, XAML preview, and NativeAOT diagnostics. VS Code + Claude Code remains the agentic workflow environment.

### Recommended Extensions

| Extension | Purpose |
|---|---|
| **Avalonia for Visual Studio** | XAML previewer, IntelliSense for .axaml, control templates |
| **CsWin32** | Source generator support for NativeMethods.txt editing |
| **EditorConfig Language Service** | Syntax highlighting/validation for .editorconfig naming rules |
| **SQLite/SQL Server Compact Toolbox** | Inspect history.db during development |
| **Markdown Editor** | Edit story files and docs without leaving VS |
| **Roslynator** | 500+ analyzers/refactorings on top of AnalysisLevel=latest-all |
| **SonarLint** | Security issue detection (important for admin-level system tool) |
| **Fine Code Coverage** | Inline test coverage visualization across 5 test projects |
| **ILLink Analyzer** | Surfaces trimming/NativeAOT warnings in editor before publish (if available) |

**Skip:** ReSharper (heavy; Roslynator + built-in analyzers covers it), GitHub Copilot (using Claude Code).

---

## Best Practices for This Project

### When to invoke skills

- **Before writing any Avalonia UI code:** `/avalonia-zafiro-development` is mandatory per skill definition
- **Before P/Invoke or COM work:** `/dotnet-native-interop` + `/dotnet-native-aot` (NativeAOT compatibility)
- **Before performance-sensitive code:** `/dotnet-performance-patterns`
- **During code review:** `/dotnet-best-practices` + `/dotnet-design-pattern-review`
- **When modeling ChangeDescriptor/module contracts:** `/dotnet-domain-modeling`
- **When writing DI registration:** `/dotnet-csharp-dependency-injection`

### Skill stacking

Multiple skills can be active in one session. For a typical module implementation task, invoke:
1. `avalonia-zafiro-development` (if UI involved)
2. `dotnet-csharp-modern-patterns` (C# 14 idioms)
3. `dotnet-native-aot` (NativeAOT safety check)

### MCP server usage

- Always use Avalonia DevTools `attach-to-file` for XAML validation during UI development
- Prefer `attach-to-file` over `attach-to-app` when designing layouts (no running app needed)

### Agent parallelization

- Use `Explore` subagents for codebase questions that need >3 searches
- Use `general-purpose` agents in parallel for independent file edits
- Use background agents (`run_in_background: true`) for long research while continuing other work
