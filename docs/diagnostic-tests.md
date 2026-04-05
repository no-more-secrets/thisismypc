# Diagnostic Tests

Live-system tests that read the real Windows registry and produce structured output showing exactly what the app would display. These are the primary development tool for context menu and shell module work — they let you verify handler names, tab assignments, scope routing, and toggle states without launching the GUI.

## Context Menu: Full System State Dump

**Test:** `ContextMenuDisplayDiagnosticTests.Display_full_system_state`
**Location:** `tests/ThisIsMyPC.Integration.Tests/ViewModels/ContextMenuDisplayDiagnosticTests.cs`

Reads the real registry, runs the full scanner + ViewModel pipeline, and dumps every handler by tab.

### How to run

```bash
dotnet test tests/ThisIsMyPC.Integration.Tests --filter "Display_full_system_state" --no-restore --logger "console;verbosity=detailed"
```

### What it shows

For every handler in every tab (Multi, File, Folder, Folder Background, Desktop, Misc):

- Toggle state (ON/OFF/--- for non-toggleable)
- Handler type badge (COM Handler, Static Verb, Modern Packaged, Drag-Drop)
- Display name (as the user sees it)
- CLSID
- All scopes and registry paths
- DLL path
- Warning text, disable method, toggle-disabled reason
- Scope badges (Multi tab only)
- Static verb details (command line, DelegateExecute, flags like Shift-only/UAC)
- Orphan status and reason

Summary section shows:
- Tab counts
- Handler type breakdown (COM, static verb, modern, drag-drop, orphaned, dual-registered)
- Toggleable vs non-toggleable lists with CLSIDs
- Classic context menu shim state

### When to use

- **Before any context menu UI change** — run the dump to see the real state, verify display names, check tab assignments
- **After changing tab routing or display name logic** — compare before/after output
- **To identify unknown handlers** — the output shows command lines, DLL paths, and CLSIDs that map to real menu entries
- **To verify toggle-ability** — the toggleable/non-toggleable breakdown shows exactly which handlers can be controlled

### Example: identifying an unknown verb

The dump might show:
```
[ON ] [Static Verb     ] AnyCode
       VerbInfo:     Cmd:"C:\Program Files (x86)\Common Files\Microsoft Shared\MSEnv\VSLauncher.exe" "%V"
```

The command line reveals this is Visual Studio's "Open with Visual Studio" launcher — the verb name `AnyCode` doesn't tell you that, but `VSLauncher.exe` does.

### Example: checking Multi tab routing

After the scope-based routing fix, handlers registered at one scope that maps to multiple UI tabs (e.g., `Directory\Background` maps to both Folder Background and Desktop) stay in those specific tabs. Only handlers registered at 2+ distinct scopes go to Multi:

```
Tabs: File (13), Folder (12), Folder Background (7), Desktop (13), Multi (15), Misc (11)
```

## Static Verb Scan

**Test:** `StaticVerbIntegrationTests.Diagnostic_scan_real_registry_static_verbs`
**Location:** `tests/ThisIsMyPC.Integration.Tests/Registry/StaticVerbIntegrationTests.cs`

Lower-level dump of raw static verb entries from the 7 HKCR scope paths. Shows verb metadata before the scanner processes it.

```bash
dotnet test tests/ThisIsMyPC.Integration.Tests --filter "Diagnostic_scan_real_registry_static_verbs" --no-restore --logger "console;verbosity=detailed"
```

## Adding New Diagnostic Tests

When building features that interact with the real system (new handler types, new scope paths, new toggle mechanisms), add a diagnostic test that:

1. Uses real `RegistryService` (not fake)
2. Runs the actual scanner/service pipeline
3. Dumps structured output via `ITestOutputHelper`
4. Includes enough detail to identify real-world entries (command lines, DLL paths, CLSIDs)

Mark diagnostic tests with `[Trait("Category", "Diagnostic")]` so they can be filtered separately from unit tests.
