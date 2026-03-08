# Toby — Project Setup

Did you nose thats, if you wants to work on the project, this is how?

## Stack

- .NET / C# / Avalonia UI with Zafiro toolkit
- NativeAOT target
- Heavy P/Invoke and COM interop (Windows system APIs, DDC/CI, ATKACPI, HID)

## Claude Code Skills

Installed at `~/.claude/skills/` (symlinked from `~/.agents/skills/`). Find them on Claude Code marketplaces

### Avalonia / UI (mandatory for UI work)

- `avalonia-zafiro-development` — Core conventions, Zafiro toolkit rules, functional-reactive MVVM
- `avalonia-layout-zafiro` — Shared styles, layout patterns, Zafiro components
- `avalonia-viewmodels-zafiro` — ViewModel lifecycle, ReactiveUI patterns, wizard flows

### .NET (use as needed)

- `dotnet-native-interop` — P/Invoke, COM, LibraryImport, marshalling
- `dotnet-native-aot` — NativeAOT compatibility (this project targets AOT)
- `dotnet-csharp-modern-patterns` — C# 12-15 idioms
- `dotnet-csharp-dependency-injection` — MS DI, keyed services, hosted services
- `dotnet-performance-patterns` — Span, ArrayPool, ref struct, stackalloc
- `dotnet-best-practices` — Code review

Stack them per session — typical module work: `avalonia-zafiro-development` + `dotnet-csharp-modern-patterns` + `dotnet-native-aot`. Add `dotnet-native-interop` for system API calls.

## Avalonia DevTools MCP

Live XAML inspection without running the app. Add to your MCP config:

```json
{
  "avalonia_devtools": {
    "type": "stdio",
    "command": "avdt",
    "args": ["mcp"]
  }
}
```

Install `avdt` CLI globally. Project must reference `AvaloniaUI.DiagnosticsSupport` NuGet package.

Use `attach-to-file` for layout work (no running app needed), `attach-to-app` for runtime debugging. After editing XAML, call `tree`/`search` again to refresh node IDs.

## Goals

Basically, just do your funny Toby ahh magic because your lowk named after bourne again shell so youre basically a computer🤓 thanks tobini