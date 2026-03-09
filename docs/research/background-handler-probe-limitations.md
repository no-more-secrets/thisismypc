# Background Handler COM Probe — Limitations & Failed Approaches

**Date:** 2026-03-08
**Related:** `docs/research/background-handler-surface-detection.md`
**Status:** Open — 2 false positives remain, deferred to further research

---

## Summary

The COM probe (`IContextMenuProbe`) correctly filters 4 of 7 `Directory\Background` handlers on Sam's system but produces false positives for 2 handlers that always add menu items regardless of probe parameters. Windows Explorer filters these through an internal mechanism we haven't identified.

## Probe accuracy (Sam's system, 2026-03-08)

| Handler | CLSID | Expected | Probe says | Verdict |
|---|---|---|---|---|
| NvAppDesktopContext | `{F2E8B4A1-...}` | Desktop only | Desktop only | Correct |
| Sharing | `{f81e9010-...}` | Not on Desktop | Not on Desktop | Correct |
| DesktopSlideshow | `{0bf754aa-...}` | Desktop only (DesktopBackground path) | Desktop only | Correct |
| New | `{D969A300-...}` | Both surfaces | Both surfaces | Correct |
| FileSyncEx | `{CB3D0F55-...}` | Unknown | Both surfaces | Unknown |
| WorkFolders | `{E61BF828-...}` | Unknown | Both surfaces | Unknown |
| PowerRenameExt | `{0440049F-...}` | Folder BG only | Both surfaces | **False positive** |
| NvCplDesktopContext | `{3D1975AF-...}` | Desktop only | Both surfaces | **False positive** |

## What works

The probe correctly detects handlers that check the PIDL in `IShellExtInit::Initialize`:
- **NvAppDesktopContext** rejects the Documents PIDL (returns failure from Initialize) → correctly excluded from Folder Background
- **Sharing** rejects the Desktop PIDL → correctly excluded from Desktop

The `SHGetKnownFolderIDList` approach with virtual namespace PIDLs (`FOLDERID_Desktop` for desktop, `FOLDERID_Documents` for folder) gives the best results across all handlers tested.

## What we tried that didn't work

### 1. IDataObject via CIDLData_CreateFromIDArray

**Theory:** Handlers check the IDataObject parameter of `IShellExtInit::Initialize` to determine surface context. Windows passes a real IDataObject wrapping the folder PIDL.

**Implementation:** Called `CIDLData_CreateFromIDArray(pidl, 0, null, out pDataObject)` to create a data object from the folder PIDL with zero child items (correct for background context — no selected items). Passed as 2nd parameter to Initialize.

**Result:** No change in probe results for any handler. PowerRenameExt and NvCplDesktopContext still show on both surfaces.

**Conclusion:** These handlers don't use IDataObject for surface filtering.

### 2. hkeyProgID via RegOpenKeyExW

**Theory:** Handlers read the hkeyProgID registry handle (3rd parameter of `IShellExtInit::Initialize`) to determine which surface they're serving. Windows passes `HKCR\Directory\Background` for folder surfaces and `HKCR\DesktopBackground` for desktop.

**Implementation:** Called `RegOpenKeyExW(HKEY_CLASSES_ROOT, "Directory\\Background", ...)` for folder background and `RegOpenKeyExW(HKEY_CLASSES_ROOT, "DesktopBackground", ...)` for desktop. Passed as 3rd parameter to Initialize.

**Result:** No change in probe results for any handler.

**Conclusion:** These handlers don't use hkeyProgID for surface filtering. This is expected — all these handlers are registered under `Directory\Background`, so Windows would pass the same hkeyProgID regardless of surface.

### 3. File system PIDLs via ILCreateFromPathW

**Theory:** `SHGetKnownFolderIDList(FOLDERID_Desktop)` returns a virtual namespace root PIDL (empty PIDL), not a file system PIDL for `C:\Users\user\Desktop`. Handlers might need the actual file system PIDL to recognize the desktop.

**Implementation:** Called `SHGetKnownFolderPath` to get the file system path (e.g., `C:\Users\user\Desktop`), then `ILCreateFromPathW` to create a proper file system absolute PIDL.

**Result:** Made things **worse**. Both NVIDIA handlers now appeared on Folder Background (previously NvAppDesktopContext was correctly filtered). FileSyncEx and Sharing now appeared on Desktop (previously Sharing was correctly filtered).

**Conclusion:** The virtual namespace PIDLs from `SHGetKnownFolderIDList` are the CORRECT PIDLs for this use case. Handlers like NvAppDesktopContext and Sharing DO check the PIDL — they specifically distinguish between the virtual desktop PIDL and the virtual Documents PIDL. File system PIDLs lose this distinction because both are regular file system folders.

### 4. All three combined (IDataObject + hkeyProgID + virtual PIDLs)

Also tried combining IDataObject and hkeyProgID with the original virtual PIDLs. No improvement over virtual PIDLs alone.

## Why 2 handlers still produce false positives

PowerRenameExt and NvCplDesktopContext add menu items to the scratch HMENU via `QueryContextMenu` regardless of which PIDL, IDataObject, or hkeyProgID they receive. They don't filter at the `IShellExtInit`/`IContextMenu` COM interface level.

Windows Explorer must filter them through one of these possible mechanisms:

1. **IObjectWithSite** — Explorer might set a site object on the handler that provides additional context. Some handlers call `IObjectWithSite::GetSite` to query for Explorer-specific interfaces.

2. **IContextMenu2/IContextMenu3 post-processing** — After `QueryContextMenu`, Explorer might call `HandleMenuMsg`/`HandleMenuMsg2` which gives handlers a chance to modify or remove items based on additional context.

3. **Explorer-internal handler registry** — Explorer might maintain a private list of which handlers to show per surface, separate from the COM activation path.

4. **Shell folder view integration** — The handler might be filtered by the `IShellView` or `IShellBrowser` that hosts the context menu. Desktop and folder views might implement different filtering policies.

5. **CMF flags or undocumented QueryContextMenu parameters** — We pass `CMF_NORMAL` (0). Explorer might pass additional flags or use `IContextMenu3::GetCommandString` to filter after the fact.

## Current behavior (shipping as-is)

The false positives are **safe** — they show handlers on an extra tab, which is the same as the pre-probe behavior for those handlers. No handler is incorrectly hidden from a tab where it should appear.

Net improvement: 4 handlers correctly filtered that were previously shown identically on both tabs.

## Research needed

To resolve the remaining false positives, investigate:
- How Windows Explorer builds context menus for `Directory\Background` handlers on the actual desktop surface
- Whether `IObjectWithSite` or `IFolderView` provide surface context to handlers
- PowerToys source code for PowerRenameExt (open source) — how does it know not to show on the desktop?
- NVIDIA shell extension behavior — NvCplDesktopContext vs NvAppDesktopContext (both desktop-named, but only one filters correctly)
