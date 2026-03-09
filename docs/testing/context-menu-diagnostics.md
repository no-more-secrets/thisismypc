# Context Menu Diagnostic Tests

Automated tests that exercise the real ViewModel layer against live registry data, outputting every display property the UI would render. Use these to verify context menu display without launching the app.

## Run Commands

**Full display dump** (all tabs, badges, registry view, summary):
```
dotnet test tests/ThisIsMyPC.Integration.Tests/bin/Debug/net10.0/ThisIsMyPC.Integration.Tests.dll --filter "ContextMenuDisplayDiagnosticTests" --logger "console;verbosity=detailed"
```

**Static verb scanner diagnostics** (raw scan, dedup report, scanner output, sandbox toggle):
```
dotnet test tests/ThisIsMyPC.Integration.Tests/bin/Debug/net10.0/ThisIsMyPC.Integration.Tests.dll --filter "StaticVerbIntegrationTests" --logger "console;verbosity=detailed"
```

If the App exe is locked (running in VS), use `--no-build` or build with `--no-dependencies` first:
```
dotnet build tests/ThisIsMyPC.Integration.Tests -no-dependencies --no-restore
```

## What Each Test Outputs

### `ContextMenuDisplayDiagnosticTests.Display_all_context_menu_entries`

Instantiates `ContextMenuViewModel` with real scan data and dumps every handler VM property per tab:

- **Per handler:** enabled state, handler type badge, label, description, warning text, disable method, scope note, verb flags (Shift-only, UAC, Script-only, Position, DelegateExecute)
- **Registry view mode:** first 5 entries showing verb name, command, DelegateExecute CLSID, AppliesTo
- **Summary:** unique VM count by type, tab count strings

### `StaticVerbIntegrationTests.Diagnostic_scan_real_registry_static_verbs`

Raw `StaticVerbService` output grouped by scope: verb name, MUIVerb, registry path, command line, all flags. Summary stats (command-only, DelegateExecute-only, both, DropTarget, no-execution, disabled, extended).

### `StaticVerbIntegrationTests.Diagnostic_scan_deduplication_report`

Lists verbs registered at multiple scope paths (dedup candidates) with all their registrations.

### `StaticVerbIntegrationTests.Diagnostic_scanner_unified_output`

Full `ContextMenuScanner` pipeline output: deduplicated handlers with classification, scopes, path count, enabled state, verb flags.

### `StaticVerbIntegrationTests.Sandbox_toggle_LegacyDisable_write_and_delete`

Creates a verb in `HKCU\Software\ThisIsMyPC\Tests\StaticVerbs\`, writes LegacyDisable, re-scans to confirm disabled, deletes LegacyDisable, re-scans to confirm enabled. Cleans up automatically.

### `StaticVerbIntegrationTests.Sandbox_scan_reads_all_metadata`

Creates a verb with all metadata fields populated, scans, and asserts every field was read correctly.

## Audit Workflow

To verify scanner accuracy against real Windows context menus:

1. **Run both diagnostic tests** to get current scanner output
2. **Cross-reference against the catalog**: `docs/research/context-menu-catalog.md` — the Scanner Cross-Reference section maps catalog entries to handler names
3. **Verify registry values** for any discrepancies using `reg query "HKCR\...\shellex\ContextMenuHandlers\<name>"`
4. **Check display names**: verb key default values provide the display text when MUIVerb is absent; indirect strings (`@dll,-ID`) remain unresolved

Key things to check for:
- **Inverted CLSID registrations**: key name = CLSID, default value = friendly name (e.g., Taskband Pin, Start Menu Pin)
- **Tab placement**: `Directory\Background` static verbs appear on both Folder Background AND Desktop tabs; COM handlers at the same path use probe filtering
- **Display name precedence**: MUIVerb > default value of verb key (stripped of `&` mnemonics, skip `@` indirect strings) > verb key name

## Test File Locations

- `tests/ThisIsMyPC.Integration.Tests/ViewModels/ContextMenuDisplayDiagnosticTests.cs`
- `tests/ThisIsMyPC.Integration.Tests/Registry/StaticVerbIntegrationTests.cs`
- `docs/research/context-menu-catalog.md` (visual observation catalog + scanner cross-reference)
