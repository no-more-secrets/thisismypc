# Context Menu Scanner Rationale

Design rationale for the context menu scanner in `ThisIsMyPC.Modules.Shell`. This is AI-written research, consolidated from four earlier research documents and corrected against the code as of 2026-09-01. It explains why the scanner reads the registry paths it reads, how it classifies handlers, how it decides which background surface a handler appears on, and what it cannot detect. Every claim about the scanner was checked against `src/` and `tests/`. The original observations came from one personal machine whose exports are not in the repo.

## 1. Surfaces and registration paths

The scanner collects four handler types and merges them into one list (`ContextMenuScanner.Scan`).

### COM handlers (`shellex\ContextMenuHandlers`)

`ShellExtensionService.HandlerRegistrations` reads these 12 paths. Each subkey's default value is a CLSID; the DLL path comes from `HKCR\CLSID\{clsid}\InprocServer32`.

| Registry path | Scope label |
|---|---|
| `HKCR\*\shellex\ContextMenuHandlers` | All files |
| `HKCR\AllFilesystemObjects\shellex\ContextMenuHandlers` | All filesystem objects |
| `HKCR\Directory\shellex\ContextMenuHandlers` | Directories |
| `HKCR\Directory\Background\shellex\ContextMenuHandlers` | Folder background |
| `HKCR\Folder\shellex\ContextMenuHandlers` | Folders |
| `HKCR\Drive\shellex\ContextMenuHandlers` | Drives |
| `HKCR\DesktopBackground\shellex\ContextMenuHandlers` | Desktop background |
| `HKCR\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\shellex\ContextMenuHandlers` | Recycle Bin |
| `HKCR\CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\shellex\ContextMenuHandlers` | This PC |
| `HKCR\CLSID\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}\shellex\ContextMenuHandlers` | Network |
| `HKCR\SystemFileAssociations\Directory.Audio\shellex\ContextMenuHandlers` | Audio folders |
| `HKCR\SystemFileAssociations\Directory.Video\shellex\ContextMenuHandlers` | Video folders |

The three `HKCR\CLSID\{...}` paths were empty on the research machine. They stay in the list because they are documented registration points and cost one failed key open each. Handlers under the two `SystemFileAssociations\Directory.*` paths are tagged `IsContentInspecting` because they read folder contents before the menu opens.

Registrations of one CLSID at several paths merge into one handler with `AllRegistryPaths`, `AllScopes`, and per-path enabled state. Paths that held only Microsoft handlers on the research machine (`Printers`, `LibraryFolder`, `UserLibraryFolder`, `OpenSearchProvider`) are not scanned; no third-party handler was seen there.

### Static verbs (`shell\<verb>`)

`ShellRegistryPaths.StaticVerbScopePaths` reads seven fixed scopes: `*`, `AllFilesystemObjects`, `Directory`, `Folder`, `Directory\Background`, `DesktopBackground`, `Drive`. `StaticVerbService` reads per verb: `MUIVerb`, `Icon`, `Position`, `Extended`, `LegacyDisable`, `ProgrammaticAccessOnly`, `AppliesTo`, `HasLUAShield`, the `command` default value, and `command\DelegateExecute`. Verbs with the same name and the same command (or DelegateExecute CLSID) at several scopes merge into one entry.

The remaining three verb scopes (`SystemFileAssociations\<type>`, `.ext`, `<ProgID>`) are read per file type on demand. `ProgIdResolver.Resolve(extension)` builds the chain: default ProgID, `OpenWithProgids` entries, `SystemFileAssociations\.ext`, then `SystemFileAssociations\<PerceivedType>`. `FileTypeVerbService` scans verbs and `shellex\ContextMenuHandlers` along that chain (`ContextMenuScanner.ScanFileType`).

`InternalHandlerFilter` drops verbs that never produce a visible entry: `explore`, `open`, `find`, `removeproperties`, `opennewprocess`, `opennewtab`, `opennewwindow`, and the two Spotlight desktop verbs.

### Drag-drop handlers

`ShellExtensionService.EnumerateDragDropHandlers` reads `shellex\DragDropHandlers` under `*`, `Directory`, and `Folder`. The drag-right-click menu is a separate invocation path with its own small menu; only legacy `IContextMenu` handlers take part in it.

### Modern packaged handlers

`ModernPackagedHandlerService` opens `AppExtensionCatalog` `windows.fileExplorerContextMenus` and reads each package's `ItemType` and `Verb` data. A modern handler whose CLSID also appears as a COM handler is marked `IsDualRegistered` on both entries. Modern handlers cannot be disabled from the registry; `DisableMethod` stays `None`.

### Disable mechanisms

| Method | Registry effect | Scope |
|---|---|---|
| Dash prefix | Default value `{clsid}` becomes `-{clsid}` at every registration path | COM handler, per path |
| Blocked list | Value named `{clsid}` under `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked` | COM handler, machine-wide |
| `LegacyDisable` | Empty value under the verb key | Static verb, per scope |

`ContextMenuChangeFactory` applies the dash prefix at all `AllRegistryPaths` so a multi-registered handler is disabled everywhere at once. A handler is enabled only when every path is enabled and the CLSID is not on the blocked list.

### Tab assignment

`ContextMenuTab.GetTabs` maps scope labels to tabs: All files and All filesystem objects to File; Directories and Folders to Folder; Folder background to the probe result (section 3); Desktop background to Desktop; Drives, This PC, Network, Recycle Bin, Audio folders, and Video folders to Misc. Static verbs under `Directory\Background` show on both Folder Background and Desktop, because Explorer inherits static verbs to the desktop. Modern handlers under `Directory\Background` show on Folder Background only.

## 2. Handler classification

`HandlerClassification` has four values. `ContextMenuHandlerClassifier.Classify` applies them in this order.

| Class | Rule |
|---|---|
| Critical | CLSID is in the fixed critical set below |
| Optional | Company name contains "Microsoft" and DLL path contains "PowerToys"; or no version info and path contains "PowerToys" |
| System | Company name (from the DLL version resource) contains "Microsoft" |
| ThirdParty | Everything else, including handlers with no DLL path |

The critical set is these ten CLSIDs. Disabling any of them removes a core Explorer action.

| CLSID | Handler | Why critical |
|---|---|---|
| `{09799AFB-AD67-11d1-ABCD-00C04FC30936}` | Open With | Removes the Open with submenu |
| `{7BA4C740-9E81-11CF-99D3-00AA004AE837}` | SendTo | Removes Send to |
| `{D969A300-E7FF-11d0-A93B-00A0C90F2719}` | New Menu | Removes New on folder and desktop backgrounds |
| `{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}` | Copy as Path | Removes Copy as path |
| `{00021401-0000-0000-C000-000000000046}` | Shortcut (.lnk) | Breaks shortcut context menus |
| `{85cfccaf-2d14-42b6-80b6-f40f65d016e7}` | Shortcut (.symlink) | Breaks symlink context menus |
| `{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}` | Sharing | Removes Give access to |
| `{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}` | Start Menu Pin | Removes Pin to Start |
| `{90AA3A4E-1CBA-4233-B8BB-535773D48449}` | Taskband Pin | Removes Pin to taskbar |
| `{A470F8CF-A1E8-4f65-8335-227475AA5C46}` | Encryption | Removes EFS encrypt and decrypt |

The two pin handlers use an inverted registration: the key name is the CLSID and the default value is the friendly name. `ShellExtensionInfo.EffectiveRegistryKeyName` handles that case.

Static verbs classify by `ClassifyStaticVerb`: canonical verbs (`open`, `print`, `explore`, `properties`) are Critical; commands under `\Windows\`, `\SystemRoot\`, or starting with `explorer` are System; commands containing "PowerToys" are Optional; verbs with no command line are System (every DelegateExecute-only verb in the audit was a Microsoft shell verb); the rest are ThirdParty. Modern packaged handlers classify by package family name prefix (`Microsoft.`, `Windows.`) or publisher display name.

The PowerToys split exists because those handlers are Microsoft-signed but user-installed. Labelling them Optional tells the user they are safe to disable without a System warning.

## 3. Background surface probing

`HKCR\Directory\Background\shellex\ContextMenuHandlers` serves both the folder background and the desktop background. The registry cannot say which surface a handler targets; the handler decides at runtime. Before probing, the scanner showed every background handler on both tabs.

Explorer builds a background menu like this:

1. Creates a shell item for the surface (a folder PIDL, or the desktop PIDL).
2. Instantiates each registered handler with `CoCreateInstance`.
3. Calls `IShellExtInit::Initialize(pidlFolder, pDataObject, hkeyProgID)`.
4. Calls `IContextMenu::QueryContextMenu` on the menu.
5. Drops the handler when Initialize fails or QueryContextMenu adds zero items.

`ContextMenuProbe.HandlerAppearsOnSurface` repeats steps 2 to 5 against a scratch `HMENU`. It passes the virtual namespace PIDL from `SHGetKnownFolderIDList` (`FOLDERID_Desktop` for the desktop, `FOLDERID_Documents` for a folder), null for the data object, and null for `hkeyProgID`. `ContextMenuScanner.ProbeSurfaceVisibility` probes only handlers whose scopes include Folder background, and adds Desktop when the handler is also registered under `DesktopBackground`. The probe runs once per scan, not per tab switch.

The probe uses `[LibraryImport]` and raw vtable calls, not CsWin32, so it is NativeAOT-safe.

Safety model:

| Failure point | Result | Reason |
|---|---|---|
| CLSID does not parse | Both surfaces | Cannot probe |
| `CoCreateInstance` fails | Both surfaces | Cannot probe |
| `SHGetKnownFolderIDList` fails | Both surfaces | Infrastructure failure |
| `Initialize` fails | Not on this surface | Matches Explorer |
| `QueryInterface(IContextMenu)` fails | Both surfaces | Cannot probe |
| `QueryContextMenu` fails | Both surfaces | Ambiguous |
| Zero items added | Not on this surface | Handler filtered itself out |
| Exception | Both surfaces | Unknown state |
| Empty result set | Both surfaces | Mapper fallback |
| No probe registered | Both surfaces | Backward compatible |

Every fallback shows the handler on more tabs, never fewer. A false positive costs one extra tab entry; a false negative would hide a handler the user needs to find.

## 4. Probe limitations

On the research machine the probe filtered four of seven `Directory\Background` handlers correctly and produced two false positives. Two handlers (the PowerToys PowerRename extension and a GPU vendor control-panel extension) add items on both surfaces no matter what the probe passes, although Explorer shows each on only one surface.

Handlers that the probe filters correctly check the PIDL inside `Initialize`. The virtual desktop PIDL and the virtual Documents PIDL are distinct, and those handlers reject one of them.

Four alternatives were tried and rejected:

| Attempt | Theory | Result |
|---|---|---|
| Real `IDataObject` via `CIDLData_CreateFromIDArray` (folder PIDL, zero children) | Handlers read the data object for surface context | No change |
| `hkeyProgID` from `RegOpenKeyExW` on `Directory\Background` or `DesktopBackground` | Handlers read the ProgID key | No change; both surfaces share one registration, so Explorer passes the same key anyway |
| File-system PIDLs via `ILCreateFromPathW` on the real desktop and documents folders | Handlers need a file-system PIDL | Worse: handlers that had filtered correctly stopped filtering, because a file-system desktop PIDL looks like any other folder |
| All three combined with virtual PIDLs | Cumulative | No improvement over virtual PIDLs alone |

The two remaining handlers do not filter at the `IShellExtInit` or `IContextMenu` level. Explorer must exclude them by another route. Candidates not yet tested: `IObjectWithSite` (a site object that exposes the hosting view), `IContextMenu2` or `IContextMenu3` message handling after `QueryContextMenu`, an Explorer-internal per-surface list, `IShellView` or `IShellBrowser` filtering, or `CMF_*` flags other than `CMF_NORMAL`. The PowerToys source is open and is the best place to look for how PowerRename detects the desktop.

The false positives ship as-is. They put a handler on one extra tab, which matches the pre-probe behaviour for that handler, and no handler is hidden from a tab where Explorer shows it.

## 5. Observed menu structure

Menus were recorded by hand on one Windows 11 Pro machine (March 2026) for an image file, an HTML file, a PDF, a folder, a folder background, the desktop background, a drag-right-click, and files and folders inside a cloud-synced folder. Findings that shaped the scanner:

- **Persistent core.** The lower half of every file menu is the same regardless of file type: Open with, Give access to, Copy as path, Share, Restore previous versions, Send to, then the shell built-ins (Cut, Copy, Create shortcut, Delete, Rename, Properties). These come from `*` and `AllFilesystemObjects` handlers plus Explorer itself. The scanner does not list shell built-ins; they are not registry entries.
- **Global registrations are the bloat.** Archivers, editors, and file-tool vendors register under `*`, so their entries appear on every file type. These are the main targets on the File tab.
- **Per-type vendor handlers.** A PDF suite registered a different handler for each file type it cared about instead of one global handler. One toggle cannot clean it up; the Per File Type tab exists for this.
- **Duplicate entries.** One utility suite registered the same handler under two registration paths, so its entry appeared twice on every file and folder menu. The scanner's merge by CLSID shows this as one handler with two paths.
- **Background menus are lean.** Few vendors register under `Directory\Background`; the desktop background menu was the shortest (13 items). GPU vendor entries are registered at `Directory\Background` but self-filter to the desktop (section 3).
- **Cloud sync injection.** The sync client's single COM handler injects a context-dependent block (Share, Copy link, Manage access, View online, Version history or Folder colour) only when the item lives in a synced folder. The scanner shows the handler once; it cannot predict which items the handler will inject.
- **Orphaned media-player verbs.** Legacy media-player folder verbs persist on stock Windows 11 installs. `ContextMenuScanner.DetectOrphans` flags COM handlers whose DLL is missing or whose CLSID has no `HKCR\CLSID` entry; verbs whose target application is gone are not flagged.
- **Drag-right-click is separate.** Six items: Copy here, Move here (omitted when the drop target is the source folder), Create shortcuts here, plus archiver drag handlers. Only `DragDropHandlers` registrations take part.
- **Hidden and conditional entries.** The scanner finds handlers the menu never shows: `ProgrammaticAccessOnly` verbs, `Extended` (Shift-only) verbs such as the terminal launchers, slideshow and Spotlight entries that depend on wallpaper state, and the inverted-registration pin handlers.

## 6. Verification workflow

- `docs/testing/context-menu-diagnostics.md` describes the diagnostic tests that dump every tab and the raw static verb scan, and the audit workflow for comparing scanner output against a real right-click.
- `tools/audit-static-verbs.ps1` enumerates all ten static verb scopes from an elevated prompt and writes a markdown report to `artifacts/`. Use it to compare a machine's verb registrations against what `StaticVerbService` and `FileTypeVerbService` read. The report contains local paths; it is gitignored.
- `tests/ThisIsMyPC.Integration.Tests/Shell/ContextMenuOrphanIntegrationTests.cs` checks orphan detection against the live registry.
- `tests/ThisIsMyPC.Modules.Shell.Tests/Services/ContextMenuScannerTests.cs` and `ContextMenuHandlerClassifierTests.cs` cover merge, disable method, probe mapping, and classification with fakes.
