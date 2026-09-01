# Context Menu Diagnostic Tests

Live-system tests that read the real Windows registry, run the scanner and ViewModel pipeline, and dump every display property the UI would render. They exist for registry-state dumps: verifying handler names, tab assignments, scope routing, and toggle states.

The primary UI verification tool is the sight harness in `tests/ThisIsMyPC.App.UiTests`: headless Avalonia with Skia that saves PNG screenshots of the rendered views. Use the harness to check what the UI looks like; use these diagnostics to check what the scanner found.

All tests here carry `[Trait("Category", "Diagnostic")]` or `[Trait("Category", "Integration")]`, so CI skips them.

## Run Commands

Full display dump (all tabs, badges, registry view, summary):

```
dotnet test tests/ThisIsMyPC.Integration.Tests --filter "Display_full_system_state" --logger "console;verbosity=detailed"
```

Per file type tab dump:

```
dotnet test tests/ThisIsMyPC.Integration.Tests --filter "Display_per_file_type_state" --logger "console;verbosity=detailed"
```

Static verb scanner diagnostics (raw scan, dedup report, scanner output, sandbox toggle):

```
dotnet test tests/ThisIsMyPC.Integration.Tests --filter "StaticVerbIntegrationTests" --logger "console;verbosity=detailed"
```

If the App exe is locked by a running debugger, add `--no-build` after building the test project alone:

```
dotnet build tests/ThisIsMyPC.Integration.Tests --no-dependencies --no-restore
```

## What Each Test Outputs

### `ContextMenuDisplayDiagnosticTests.Display_full_system_state`

Instantiates `ContextMenuViewModel` with real scan data and dumps every handler by tab (Multi, File, Folder, Folder Background, Desktop, Misc).

Per handler:

- Toggle state (ON, OFF, or `---` for non-toggleable)
- Handler type badge (COM Handler, Static Verb, Modern Packaged, Drag-Drop)
- Display name as the user sees it
- CLSID
- All scopes and registry paths
- DLL path
- Warning text, disable method, toggle-disabled reason
- Scope badges (Multi tab only)
- Static verb details (command line, DelegateExecute, flags such as Shift-only and UAC)
- Orphan status and reason

Summary section:

- Tab counts
- Handler type breakdown (COM, static verb, modern, drag-drop, orphaned, dual-registered)
- Toggleable and non-toggleable lists with CLSIDs
- Classic context menu shim state

### `ContextMenuDisplayDiagnosticTests.Display_per_file_type_state`

Same dump for the Per File Type tab: handlers registered under a ProgID rather than a global scope.

### `StaticVerbIntegrationTests.Diagnostic_scan_real_registry_static_verbs`

Raw `StaticVerbService` output grouped by scope path: verb name, MUIVerb, registry path, command line, all flags. Summary stats (command-only, DelegateExecute-only, both, DropTarget, no-execution, disabled, extended). This is the lower-level view, before the scanner processes entries.

### `StaticVerbIntegrationTests.Diagnostic_scan_deduplication_report`

Lists verbs registered at multiple scope paths (dedup candidates) with all their registrations.

### `StaticVerbIntegrationTests.Diagnostic_scanner_unified_output`

Full `ContextMenuScanner` pipeline output: deduplicated handlers with classification, scopes, path count, enabled state, verb flags.

### `StaticVerbIntegrationTests.Sandbox_toggle_LegacyDisable_write_and_delete`

Creates a verb in `HKCU\Software\ThisIsMyPC\Tests\StaticVerbs\`, writes LegacyDisable, re-scans to confirm disabled, deletes LegacyDisable, re-scans to confirm enabled. Cleans up automatically.

### `StaticVerbIntegrationTests.Sandbox_scan_reads_all_metadata`

Creates a verb with all metadata fields populated, scans, and asserts every field was read correctly.

## When to Use

- Before any context menu UI change: run the dump to see the real state, verify display names, check tab assignments.
- After changing tab routing or display name logic: compare before and after output.
- To identify unknown handlers: the output shows command lines, DLL paths, and CLSIDs that map to real menu entries.
- To verify toggle-ability: the toggleable and non-toggleable breakdown shows which handlers can be controlled.

### Example: identifying an unknown verb

The dump might show:

```
[ON ] [Static Verb     ] AnyCode
       VerbInfo:     Cmd:"C:\Program Files (x86)\Common Files\Microsoft Shared\MSEnv\VSLauncher.exe" "%V"
```

The verb name `AnyCode` says nothing; the command line shows it is Visual Studio's launcher.

### Example: checking Multi tab routing

Handlers registered at one scope that maps to multiple UI tabs (`Directory\Background` maps to both Folder Background and Desktop) stay in those tabs. Only handlers registered at two or more distinct scopes go to Multi.

## Audit Workflow

To verify scanner accuracy against real Windows context menus:

1. Run the display dump and the static verb diagnostics to get current scanner output.
2. Cross-reference against `docs/research/context-menu-scanner-rationale.md`, which maps observed menu entries to handler names.
3. Verify registry values for any discrepancy with `reg query "HKCR\...\shellex\ContextMenuHandlers\<name>"`.
4. Check display names: verb key default values provide the display text when MUIVerb is absent; indirect strings (`@dll,-ID`) remain unresolved.

Key things to check:

- Inverted CLSID registrations: key name = CLSID, default value = friendly name (Taskband Pin, Start Menu Pin).
- Tab placement: `Directory\Background` static verbs appear on both Folder Background and Desktop tabs; COM handlers at the same path use probe filtering.
- Display name precedence: MUIVerb, then the verb key default value (stripped of `&` mnemonics, skipping `@` indirect strings), then the verb key name.

## Adding New Diagnostic Tests

When building features that interact with the real system (new handler types, new scope paths, new toggle mechanisms), add a diagnostic test that:

1. Uses the real `RegistryService`, not a fake.
2. Runs the actual scanner or service pipeline.
3. Dumps structured output via `ITestOutputHelper`.
4. Includes enough detail to identify real-world entries (command lines, DLL paths, CLSIDs).

Mark it with `[Trait("Category", "Diagnostic")]` so CI filters it out.

## Test File Locations

- `tests/ThisIsMyPC.Integration.Tests/ViewModels/ContextMenuDisplayDiagnosticTests.cs`
- `tests/ThisIsMyPC.Integration.Tests/Registry/StaticVerbIntegrationTests.cs`
- `docs/research/context-menu-scanner-rationale.md` (observation catalog and scanner cross-reference)
