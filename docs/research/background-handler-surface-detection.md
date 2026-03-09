# Background Handler Surface Detection — Plan

**Date:** 2026-03-08
**Epic:** 2 (Explorer & Context Menus)
**Scope:** Bug fix — Desktop and Folder Background tabs show wrong handlers
**Priority:** Must-fix before Epic 2 can ship
**Status:** IMPLEMENTED (2026-03-08)

---

## Problem

The app scans `HKCR\Directory\Background\shellex\ContextMenuHandlers` and dumps all handlers into both the Desktop and Folder Background tabs identically. But Windows itself filters which handlers appear on each surface at runtime.

**Registry dump from Sam's PC (2026-03-08):**

```
Directory\Background\shellex\ContextMenuHandlers:
  FileSyncEx       -> {CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}
  New              -> {D969A300-E7FF-11d0-A93B-00A0C90F2719}
  NvAppDesktopContext -> {F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}
  NvCplDesktopContext -> {3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}
  PowerRenameExt   -> {0440049F-D1DC-4E46-B27B-98393D79486B}
  Sharing          -> {f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}
  WorkFolders      -> {E61BF828-5E63-4287-BEF1-60B1A4FDE0E3}

DesktopBackground\shellex\ContextMenuHandlers:
  DesktopSlideshow -> {0bf754aa-c967-445c-ab3d-d8fda9bae7ef}
```

**Real-world observations (from context-menu-catalog.md):**

| Handler | Folder Background | Desktop Background |
|---------|:-:|:-:|
| PowerRenameExt | Yes | No |
| NvAppDesktopContext | No | Yes |
| NvCplDesktopContext | No | Yes |
| FileSyncEx (OneDrive) | ? | ? |
| New | Yes | Yes |
| Sharing | Yes | ? |
| WorkFolders | ? | ? |

The registry path alone can't distinguish surfaces. Handlers decide at runtime via COM.

## Solution

Use COM `IContextMenu` activation probing to detect which handlers actually respond on each surface.

### How Windows decides

When Explorer builds a context menu for a surface, it:
1. Creates a shell item representing the surface (folder path for folder background, desktop for desktop)
2. Instantiates each registered `IContextMenu` handler via `CoCreateInstance`
3. Calls `IShellExtInit::Initialize(pidlFolder, dataObject, hkeyProgID)` with the surface's PIDL
4. Calls `IContextMenu::QueryContextMenu(hmenu, ...)`
5. If the handler adds zero items or returns failure, it doesn't appear

We can replicate this: create a COM instance of each handler, initialize it with the Desktop PIDL vs a folder PIDL, call `QueryContextMenu`, and check if it adds any items.

### Technical approach

1. **Create `IContextMenuProbe` interface** in `Core.Services`:
   ```csharp
   public interface IContextMenuProbe
   {
       OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface);
   }

   public enum ContextMenuSurface { FolderBackground, DesktopBackground }
   ```

2. **Implement `ContextMenuProbe`** in `Interop.Com.Shell`:
   - Use `CoCreateInstance` to instantiate the handler's CLSID as `IContextMenu`
   - Call `IShellExtInit::Initialize` with appropriate PIDL:
     - Desktop: `SHGetKnownFolderIDList(FOLDERID_Desktop, ...)`
     - Folder: `SHGetKnownFolderIDList(FOLDERID_Documents, ...)` (any folder works)
   - Call `IContextMenu::QueryContextMenu` on a scratch HMENU
   - If items added > 0, handler appears on that surface
   - Clean up: destroy scratch menu, release COM objects

3. **P/Invoke requirements** (CsWin32):
   - `CoCreateInstance`
   - `CreatePopupMenu` / `DestroyMenu`
   - `GetMenuItemCount`
   - `SHGetKnownFolderIDList`
   - `IContextMenu`, `IShellExtInit` COM interfaces
   - Add to `NativeMethods.txt`

4. **Integration into scanner**:
   - After `ShellExtensionService.EnumerateContextMenuHandlers()` returns all `Directory\Background` handlers
   - `ContextMenuScanner` calls `IContextMenuProbe` for each handler with both surfaces
   - Populates new properties on `ContextMenuHandler` indicating which surfaces it appears on
   - `ContextMenuTabMapper` uses the probe results instead of hardcoding both tabs

5. **Update `ContextMenuTabMapper`**:
   - Remove the `"Folder background" => [FolderBackground, Desktop]` mapping
   - Replace with per-handler surface assignment based on probe results

6. **Also scan `DesktopBackground` path**:
   - Add `HKCR\DesktopBackground\shellex\ContextMenuHandlers` to `HandlerRegistrations`
   - Map to `"Desktop background"` -> `[ContextMenuTab.Desktop]`
   - Currently has `DesktopSlideshow` (`{0bf754aa}`) which we're missing

### Safety considerations

- COM activation must be done carefully — some handlers may crash, hang, or have side effects
- Use a timeout on `QueryContextMenu` (or catch exceptions per handler)
- Cache results — probe once per scan, not on every tab switch
- Handlers that fail to probe should default to appearing on BOTH surfaces (safe fallback)
- Run probing off the UI thread

### Dead registry paths to clean up

These paths have no handlers on Sam's system and may not exist on modern Win11:
- `HKCR\CLSID\{645FF040-...}\shellex\ContextMenuHandlers` (Recycle Bin)
- `HKCR\CLSID\{20D04FE0-...}\shellex\ContextMenuHandlers` (This PC)
- `HKCR\CLSID\{F02C1A0D-...}\shellex\ContextMenuHandlers` (Network)

Don't remove yet — they may exist on other systems. But deprioritize testing them.

### Files to change

**New:**
- `src/ThisIsMyPC.Core/Services/IContextMenuProbe.cs` — Interface + `ContextMenuSurface` enum
- `src/ThisIsMyPC.Interop.Com/Shell/ContextMenuProbe.cs` — COM-based implementation

**Modified:**
- `src/ThisIsMyPC.Interop.Com/Shell/ShellExtensionService.cs` — Add `DesktopBackground` path
- `src/ThisIsMyPC.Modules.Shell/Models/ContextMenuHandler.cs` — Add surface visibility properties
- `src/ThisIsMyPC.Modules.Shell/Services/ContextMenuScanner.cs` — Call probe, populate surfaces
- `src/ThisIsMyPC.App/ViewModels/ContextMenuTab.cs` — Update mapper to use probe results
- `src/ThisIsMyPC.App/ViewModels/ContextMenuViewModel.cs` — Assign tabs from surface data
- `src/ThisIsMyPC.App/App.axaml.cs` — Register `IContextMenuProbe`
- `src/ThisIsMyPC.Interop.Com/NativeMethods.txt` — Add COM/menu APIs (if using CsWin32 here too)

### Placement in Epic 2

This could be:
- **A bug fix story** (e.g., "2.2.1 — Fix background handler surface detection")
- **Folded into Story 2.8** (Modern Handler Enumeration) since it's about improving enumeration accuracy

Recommend: standalone bug fix story. Story 2.8 is about a different problem (modern IExplorerCommand handlers). This is about shellex handler surface filtering.

### Test strategy

- Unit tests with `FakeContextMenuProbe` that returns canned results
- Verify tab assignment uses probe data, not hardcoded mapping
- Integration test: real COM probe on known handlers (NVIDIA, PowerRename) — may need to be trait-marked for systems that have them

---

## Additional gaps beyond Desktop/FolderBackground split

### Missing registry path: `DesktopBackground\shell` (static verbs)

The deep research (`docs/deep-research/windows11-context-menu-research.md`) documents that `HKCR\DesktopBackground\shell` hosts static verbs for the desktop (Display settings, Personalize). We only scan `shellex\ContextMenuHandlers` under each path, not `shell` static verbs. This is by design — the app manages shellex handlers, not shell verbs. But worth noting.

### WizTree mystery — needs its own research

WizTree appears on:
- Folder right-click (catalog)
- Folder Background (catalog)
- Desktop Background (catalog)
- **This PC right-click** (user-reported)
- **Network right-click** (user-reported)

But WizTree is NOT in any of our scanned `shellex\ContextMenuHandlers` paths:
- Not in `HKCR\*`, `AllFilesystemObjects`, `Directory`, `Directory\Background`, `Folder`, `Drive`
- Not in `HKCR\CLSID\{20D04FE0-...}` (This PC) or `HKCR\CLSID\{F02C1A0D-...}` (Network) — those paths don't even exist

**Hypothesis:** WizTree may use `IExplorerCommand` with a Sparse Package manifest, which is the Win11 modern handler mechanism. This is the mechanism documented in the deep research (Section 1.1) and aligns with Story 2.8 (Modern Handler Enumeration). Alternatively, it could use a completely different registration path not yet identified.

**Action:** WizTree's registration mechanism warrants standalone research before Story 2.8 planning. Understanding how it registers will determine whether the app needs `IExplorerCommand` enumeration (significant scope) or just additional registry paths.

### Broader scanner gaps summary

| Gap | Impact | Fix location | Status |
|-----|--------|-------------|--------|
| Desktop/FolderBackground split | Tabs show wrong handlers | This plan (COM probing) | DONE |
| Missing `DesktopBackground\shellex` path | DesktopSlideshow handler not shown | This plan (add to HandlerRegistrations) | DONE |
| WizTree / IExplorerCommand handlers | Missing from all tabs | Story 2.8 + research | Open |
| `HKCR\SystemFileAssociations` per-type handlers | Unknown — may explain image-specific handlers | Needs investigation | Open |
| `HKCR\<ProgID>\shellex` per-program handlers | Unknown — may explain Adobe per-type entries | Needs investigation | Open |

### Reference

- Deep research: `docs/deep-research/windows11-context-menu-research.md` (Sections 1.1, 3.1-3.3, 4.x)
- Real-world catalog: `docs/research/context-menu-catalog.md`
- Registry dump: captured 2026-03-08, inline above

---

## Implementation Notes (2026-03-08)

### Deviations from plan

1. **Interface location:** `IContextMenuProbe` placed in `Interop.Com.Shell` (alongside `IShellExtensionService`) rather than `Core.Services`. Follows existing pattern — COM-specific interfaces live in the COM interop project.
2. **P/Invoke approach:** Used `[LibraryImport]` with raw unsafe COM vtable calls instead of CsWin32. This avoids adding a CsWin32 dependency to `Interop.Com` and is fully NativeAOT-safe with no runtime reflection.
3. **No `NativeMethods.txt`:** Not needed — P/Invoke signatures are declared directly in `ContextMenuProbe.cs` via `[LibraryImport]` source generation.

### Files actually changed

**New:**
- `src/ThisIsMyPC.Interop.Com/Shell/IContextMenuProbe.cs` — Interface + `ContextMenuSurface` enum
- `src/ThisIsMyPC.Interop.Com/Shell/ContextMenuProbe.cs` — COM vtable-based implementation

**Modified:**
- `src/ThisIsMyPC.Interop.Com/ThisIsMyPC.Interop.Com.csproj` — `AllowUnsafeBlocks`
- `src/ThisIsMyPC.Interop.Com/Shell/ShellExtensionService.cs` — Added `DesktopBackground` path
- `src/ThisIsMyPC.Modules.Shell/Models/ContextMenuHandler.cs` — Added `VisibleSurfaces` property
- `src/ThisIsMyPC.Modules.Shell/Services/ContextMenuScanner.cs` — Probes background handlers
- `src/ThisIsMyPC.Modules.Shell/ContextMenuModule.cs` — Takes `IContextMenuProbe` dependency
- `src/ThisIsMyPC.App/ViewModels/ContextMenuTab.cs` — Mapper uses probe data + "Desktop background" scope
- `src/ThisIsMyPC.App/ViewModels/ContextMenuViewModel.cs` — Passes `VisibleSurfaces` to mapper
- `src/ThisIsMyPC.App/App.axaml.cs` — DI registration

### Safety model

| Failure point | Behavior | Rationale |
|---|---|---|
| `CoCreateInstance` fails | Show on both surfaces | Can't probe — preserve current behavior |
| `SHGetKnownFolderIDList` fails | Show on both surfaces | Infrastructure failure |
| `IShellExtInit::Initialize` fails | Don't show on this surface | Matches Windows behavior |
| `QueryContextMenu` fails | Show on surface | Ambiguous — safe fallback |
| Items added = 0 | Don't show on this surface | Handler filtered itself out |
| Exception/crash | Show on both surfaces | Unknown state — safe fallback |
| Empty probe result set | Show on both surfaces | Defensive fallback in mapper |
| Null probe (no DI) | Show on both surfaces | Backward compatible |
