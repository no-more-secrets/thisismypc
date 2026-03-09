# Context Menu Research: Remaining Verification & Decisions

**Date:** 2026-03-08
**Context:** Post-synthesis of Parts 1–3 of Gemini Deep Research on Windows 11 context menu architecture
**Status:** Open — all items unresolved

---

## Verification Tasks

These are hands-on tasks that the research couldn't answer. Each requires running something on the desktop rig or reading source code directly.

### V1. Confirm the PowerRename AppxManifest.xml

**Why:** Part 3's most important finding — that PowerRename's desktop exclusion is pre-instantiation manifest scoping, not runtime GetState logic — depends on the actual contents of the AppxManifest.xml. Gemini cited a pipelines script (`versionSetting.ps1`) as its source, not the manifest itself. The architectural conclusion is almost certainly correct (PackagedCom manifest scoping is well-documented by Microsoft), but the exact file path and `<desktop5:ItemType>` declarations need confirmation.

**Task:**
1. Search the [PowerToys repo](https://github.com/microsoft/PowerToys) for the PowerRenameContextMenu manifest
2. It may be a standalone `AppxManifest.xml` or generated during build — check `/src/modules/powerrename/PowerRenameContextMenu/` first
3. If generated, find the build script or `.wapproj` that produces it
4. Confirm the `<desktop4:FileExplorerContextMenus>` node and its `<desktop5:ItemType>` entries
5. Verify that `Directory\Background` is NOT listed (which would explain desktop exclusion) or that the types listed are scoped narrowly to `*` and `Directory` only

**Expected outcome:** Either a direct link to the manifest XML showing the exact ItemType scopes, or confirmation that the manifest is build-generated with a pointer to the template/source.

**Impact if wrong:** If the manifest DOES include `Directory\Background` and PowerRename is still filtered from the desktop, then the filtering mechanism is something else entirely and the Part 3 conclusion is incorrect. This would reopen the investigation.

---

### V2. Test MFS_HIDDEN on Ghost Handlers

**Why:** Part 3 claims FileSyncEx, WorkFolders, and DesktopSlideshow insert menu items via `InsertMenuItem` but apply `MFS_HIDDEN` (`0x00000003`) to `MENUITEMINFO.fState`, making them invisible in Explorer while still detectable by a probe that only checks for item insertion. This is the proposed explanation for why the COM probe reports items added but nothing shows on screen.

**Task:**
1. In the existing COM probe, after calling `QueryContextMenu`, iterate the scratch HMENU using `GetMenuItemCount` + `GetMenuItemInfo`
2. For each item, read the `fState` member of the `MENUITEMINFO` struct
3. Check specifically for `MFS_HIDDEN` (which is `MFS_DISABLED | MFS_GRAYED`, value `0x00000003`) and `MF_DISABLED` / `MF_GRAYED` individually
4. Test against all three ghost handlers:
   - FileSyncEx `{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}`
   - WorkFolders `{E61BF828-3972-484A-B13A-E1E3A7D92E47}`
   - DesktopSlideshow `{0bf754aa-7549-4788-b787-1ca30e1895b5}`
5. Also test against the two known-visible handlers (New, PowerRenameExt) as a control — their items should NOT have MFS_HIDDEN

**Expected outcome:** Ghost handler items show `MFS_HIDDEN` in fState; visible handler items show `MFS_ENABLED` (0).

**Alternative outcomes:**
- If ghost handler items show `MFS_ENABLED` (no hidden flag), then the probe is getting different results from Explorer for a different reason — possibly the probe's `IDataObject` or PIDL is triggering fail-open behavior and the handler wouldn't insert items at all in the real Explorer environment
- If `QueryContextMenu` actually returns 0 for these handlers in the updated probe, the Part 2 probe results may have been misread

**Impact:** Determines whether the ghost handler fix is "check fState after QueryContextMenu" (simple) or "replicate Explorer's full initialization environment" (hard).

---

### V3. Test AppExtensionCatalog Enumeration

**Why:** Part 3 identifies `AppExtensionCatalog.Open("windows.fileExplorerContextMenus")` as the correct API for enumerating modern IExplorerCommand/PackagedCom entries with their surface scopes. Nobody in the research actually ran this against a real system. This is the proposed replacement for registry scraping of PackagedCom keys (which don't contain surface scope data).

**Task:**
1. Write a small C# or C++/WinRT test app
2. Call `AppExtensionCatalog.Open("windows.fileExplorerContextMenus")`
3. Iterate the returned `AppExtension` collection
4. For each extension, call `GetExtensionPropertiesAsync`
5. Dump the full `PropertySet` contents — specifically look for `ItemType` keys and `Verb`/`Clsid` mappings
6. Cross-reference against known handlers:
   - PowerRename — should show scoped to `*` and/or `Directory`, NOT `Directory\Background`
   - Windows Terminal — should show `Directory\Background` and `Directory`
   - WinRAR — should show `*` (all files)

**Expected outcome:** A complete dump of all modern context menu registrations on the system with their exact surface scopes, display names, CLSIDs, and icon references.

**Risks:**
- The API might require the calling process to have package identity itself (unlikely for a catalog query, but possible)
- The PropertySet structure might not directly mirror the manifest XML — there may be additional parsing needed
- Some entries might use indirect string references (`@{PackageName?ms-resource://...}`) that need resolution

**Impact:** If this works, it's the clean enumeration path for the entire modern handler layer. If it doesn't, fallback is parsing the AppModel State Repository SQLite DB directly (fragile, undocumented) or reading installed package manifests from `C:\Program Files\WindowsApps\`.

---

### V4. Audit Static Verb Registry Paths

**Why:** The research identifies static verbs as one of the four menu contribution sources, but the actual registry contents on the system were never fully enumerated. The context menu catalog (real menu observations) found items like Open in Terminal, Open with Visual Studio, and WizTree that aren't in the shellex layer. Part 3 provides expected paths, but these need ground-truth confirmation.

**Task:**
1. Open regedit and enumerate:
   - `HKCR\Directory\Background\shell\` — all subkeys (expected: AnyCode for VS, WizTree, possibly others)
   - `HKCR\DesktopBackground\shell\` — all subkeys (expected: Display Settings, Personalize, possibly others)
   - `HKCR\Directory\shell\` — all subkeys (expected: WizTree, cmd, PowerShell, etc.)
2. For each entry, note:
   - Subkey name (verb identifier)
   - `MUIVerb` or `(Default)` value (display text)
   - `Icon` value if present
   - `command` subkey contents (executable path + arguments)
   - Any modifier values: `Extended`, `Position`, `NeverDefault`, `ProgrammaticAccessOnly`
3. Confirm Part 3's specific claims:
   - Visual Studio at `Directory\Background\shell\AnyCode`
   - WizTree at both `Directory\shell\WizTree` AND `Directory\Background\shell\WizTree`
   - Open in Terminal as PackagedCom (should NOT appear in `shell\` keys)
4. Document anything unexpected — entries not in the catalog, or catalog entries not found in any registry path

**Expected outcome:** A complete static verb map for background surfaces that can be merged with the shellex handler data and the PackagedCom enumeration (V3) to produce a full menu reconstruction.

**Impact:** Low risk, high value. This is a 10-minute regedit task that provides immediate ground truth for the static verb layer. Any discrepancies between the registry and the real menu catalog will surface additional undocumented behavior.

---

### V5. Determine GPCache Sync Timing

**Why:** The summary (§2.3) documents that Windows Update policies require synchronizing both the standard policy keys AND the GPCache (`HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\GPCache`). The open question is whether the Update Orchestrator (`UsoSvc`) uses registry change notifications (reactive) or polls on a schedule (periodic). This affects UX — if a user toggles "disable auto-updates" and the setting doesn't take effect for hours, it undermines trust in the tool.

**Task:**
1. Modify a Windows Update policy key + corresponding GPCache value
2. Restart `UsoSvc` (`net stop UsoSvc && net start UsoSvc`) or use `sc` commands
3. Check if the Orchestrator immediately reads the new value, or if there's a delay
4. If delay, measure the interval — is it the next scheduled task run, or a fixed polling period?
5. Check for scheduled tasks under `\Microsoft\Windows\WindowsUpdate\` that might trigger GPCache reads
6. Monitor with Process Monitor (filter on `UsoClient.exe` + `UsoSvc` registry access) to see exactly when and what it reads

**Expected outcome:** Either confirmation that restarting UsoSvc forces immediate re-read (clean UX), or documentation of the polling interval/trigger mechanism (requires workaround in ThisIsMyPC).

**Impact:** Determines whether ThisIsMyPC can provide instant feedback ("setting applied") or needs to show a warning ("setting will take effect after next sync cycle") or proactively trigger a re-read.

---

## Architectural Decisions

These require judgment calls based on project priorities, engineering budget, and shipping timeline. No objectively correct answer.

### D1. Build Mock IShellBrowser Site for Legacy Probe?

**Context:** The NvCplDesktopContext false positive (and potentially other undiscovered legacy handlers) is caused by the COM probe not providing an `IObjectWithSite` → `IShellBrowser` → `IShellView` site chain. Without it, defensively-programmed handlers fail-open and insert items on all surfaces. Part 3 confirms Explorer does NOT do post-hoc HMENU stripping — the handler is responsible for self-filtering via the site chain.

**Option A: Build the mock site**
- Implement a minimal `IObjectWithSite` + `IServiceProvider` + `IShellBrowser` + `IShellView` COM mock
- Call `SetSite` on each legacy handler before `QueryContextMenu`
- Mock must convincingly answer `QueryService(SID_STopLevelBrowser)` with a fake `IShellBrowser` that returns the correct PIDL for the target surface
- **Pros:** Eliminates false positives for all site-aware legacy handlers; probe accuracy matches Explorer behavior
- **Cons:** Significant COM plumbing work; fragile (handlers may query interfaces beyond the mock's capabilities, causing crashes or unexpected behavior); needs testing against every handler on the system; ongoing maintenance as new handlers appear

**Option B: Ship with known limitation**
- Document that legacy handlers with `IObjectWithSite`-based surface filtering may appear on extra tabs
- False positives are safe (show extra, never hide things that should be visible)
- Net result: 2 known false positives on the current system (PowerRenameExt on Desktop tab, NvCplDesktopContext on Folder Background tab)
- **Pros:** Zero engineering cost; no risk of probe crashes; ships faster
- **Cons:** Users see handlers on tabs where they don't actually appear in the real menu; undermines the "accurate reconstruction" value proposition

**Option C: Hybrid — use heuristics**
- For handlers registered ONLY under `Directory\Background\shellex` (not `DesktopBackground\shellex`), check if the handler name contains "Desktop" — if so, assume desktop-only
- For handlers where the probe PIDL check already works (NvAppDesktopContext, Sharing), trust the probe
- For the remaining ambiguous handlers, show on both tabs with a visual indicator ("may not appear on this surface")
- **Pros:** Cheap to implement; handles the known cases; transparent to the user
- **Cons:** Heuristic naming checks are fragile; doesn't generalize to unknown handlers

**Recommendation from research:** Option B for initial release, Option A as Tier 2 improvement if users report confusion about handlers appearing on wrong tabs.

---

### D2. Surface Scope for Context Menu Management

**Context:** The full set of context menu surfaces in Windows 11 includes: File (per-extension and per-ProgID), Folder (direct right-click), Directory Background, Desktop Background, Drive, CompressedFolder, LibraryFolder\background, and potentially others. Full coverage requires walking the registry cascade for each surface, querying AppExtensionCatalog for modern handlers, and probing legacy COM for each.

**Option A: Full coverage (all surfaces)**
- Enumerate every surface documented in the research
- Provide per-surface tabs in the UI (matching the current Folder Background / Desktop tab approach)
- **Pros:** Complete picture; handles edge cases (Drive right-click, ZIP file context, Library backgrounds)
- **Cons:** Large engineering surface; diminishing returns (how often does a user care about their Drive right-click menu or Library background menu?)

**Option B: Common surfaces only**
- File (aggregate view showing global `*` handlers + option to filter by extension/type)
- Folder (direct right-click)
- Directory Background (whitespace inside folder)
- Desktop Background
- **Pros:** Covers ~95% of what users actually interact with; manageable scope; matches the surfaces already in the research
- **Cons:** Misses Drive, CompressedFolder, LibraryFolder; users with niche needs won't find their handlers

**Option C: Start narrow, expand by demand**
- Ship with Option B surfaces
- Add Drive and others if users request them
- Architecture should support adding surfaces easily (each surface is just a list of registry paths + inheritance rules + PackagedCom scope string)
- **Pros:** Ships faster; user-driven prioritization; clean architecture encourages future expansion
- **Cons:** Incomplete on day one; possible re-architecture if the surface model needs to change

**Recommendation:** Option C. The four common surfaces cover the vast majority of user friction. The architecture should treat surfaces as data (a surface definition is a name + a list of registry paths + an inheritance parent + a PackagedCom ItemType string), making new surfaces trivial to add later.

---

### D3. How to Present the Four Source Layers in UI

**Context:** Context menu entries come from four sources (hardcoded, static verbs, legacy COM, modern PackagedCom). A user looking at their folder background menu doesn't think in these categories — they just see a list of items. But for management purposes (enable/disable/remove), the source determines what actions are available and how they're performed.

**Option A: Unified list, source as metadata**
- Single list per surface showing all entries regardless of source
- Each entry has a badge or column indicating source type (Static, COM, Modern, System)
- Disable/remove actions adapt based on source (LegacyDisable for static, Blocked list for COM, etc.)
- **Pros:** Matches user mental model (one menu = one list); clean UI
- **Cons:** Hides the complexity that makes the system hard to manage; users may not understand why some items have different available actions

**Option B: Grouped by source**
- Per-surface view with collapsible sections: "System (cannot be modified)", "Static Verbs", "Shell Extensions", "Modern Extensions"
- **Pros:** Educates the user; makes it clear why some items can't be disabled; groups similar management actions
- **Cons:** Doesn't match what the user actually sees in their real context menu; adds cognitive overhead

**Option C: Unified list with progressive disclosure**
- Default view: unified list matching the real menu order as closely as possible
- Click/expand an entry to see source details, registry path, available actions, and any warnings
- **Pros:** Best of both — clean default view, full detail on demand; matches the "product truth engine" philosophy
- **Cons:** More UI engineering; need to figure out real menu ordering (which is partially undocumented, see Part 2 §7 on section placement)

**Recommendation:** Option C aligns best with ThisIsMyPC's identity as a tool that shows users the truth about their system. The default view should look like their actual context menu. The detail view should explain why each item is there and what can be done about it.
