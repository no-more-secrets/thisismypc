# Context Menu Research: Remaining Verification & Decisions

**Date:** 2026-03-08 (updated 2026-04-05)
**Context:** Post-synthesis of Parts 1–3 of Gemini Deep Research on Windows 11 context menu architecture
**Status:** V4 completed (2026-03-09). V2 partially resolved (2026-04-05). V1, V3, V5, D1, D2, D3 remain open.

## Session Learnings — 2026-04-05 (Story 2-14 + live testing)

Real-system testing against Sam's Windows 11 Education (25H2, [hardware] + [hardware] + [hardware]) revealed critical behavioral findings that no amount of code analysis or research could predict. These should inform all future context menu development.

### Win11 Context Menu is Multi-Layered
The visible right-click menu comes from at least 5 distinct sources. Our scanner only covers the first two at generic scope paths:
1. **COM handlers** (`shellex\ContextMenuHandlers`) — what we scan
2. **Static verbs** (`shell\<verb>`) — what we scan at 7 generic scopes
3. **Modern IExplorerCommand** (packaged apps) — enumerated via AppExtensionCatalog, not toggleable
4. **Per-file-type ProgID verbs** (e.g., `HKCR\pngfile\shell\edit`) — NOT scanned yet, needs future story
5. **Shell built-ins** (Cut, Copy, Delete, Properties) — hardwired, not toggleable

### Vestigial COM Handlers
Several registered COM handlers produce zero visible menu entries on Win11:
- **FileSyncEx** `{CB3D0F55}` — OneDrive moved entirely to IExplorerCommand. Toggling this handler has no visible effect on menu entries or overlay icons. The overlay icons come from separate Icon Overlay Handlers at `HKLM\...\ShellIconOverlayIdentifiers`.
- **AccExt** `{2A118EB5}` (Adobe Creative Cloud) — Folder-scope sync overlay, no menu entry.
- **EFS Encryption** `{A470F8CF}` — Handler exists, `cipher.exe` works, but the handler no longer surfaces a context menu entry on Win11. Encryption must be done via Properties > Advanced or command line.

### Internal Verbs Accept LegacyDisable But Ignore It
`explore`, `open`, `find`, `removeproperties`, `opennewprocess`, `opennewtab`, `opennewwindow`, `.SpotlightLearnMore`, `.SpotlightNextImage` — all accept HKCU LegacyDisable writes (the write succeeds), but Explorer ignores the flag on these hardwired verbs. These are dead switches that look functional.

### TrustedInstaller Is the Real Permission Barrier
The app requires elevation. AccessDenied always means TrustedInstaller ownership, never missing admin. The HKCU\Software\Classes overlay bypasses TrustedInstaller for static verb LegacyDisable writes (HKCR merges both hives, HKCU wins). For COM handlers, the blocked list at `HKLM\...\Shell Extensions\Blocked` is admin-writable and CLSID-wide.

### Blocked List Is CLSID-Wide
Blocking a CLSID via the blocked list removes the handler from ALL surfaces — including per-file-type registrations the scanner doesn't enumerate. More powerful than path-specific dash-prefix. Requires Explorer restart.

### Dash-Prefix Fails on System Handlers (and That's Fine)
Dash-prefix writes to TrustedInstaller-owned HKCR keys fail with AccessDenied. Since the blocked list is the authoritative mechanism, these failures are made best-effort (return Success). The toggle works — just needs Explorer restart instead of being immediate.

### Scope Inheritance
`Directory\Background` entries appear on both folder backgrounds and the desktop. This is one registry scope mapping to two UI surfaces, not two scopes. Multi-tab routing must count distinct registry scopes, not UI tab destinations.

### Conditional Visibility Depends on System State
Tested on Sam's system: EFS service is running (Manual) but handler doesn't surface UI. Work Folders service exists but isn't configured. Offline Files service exists but no network shares. Desktop slideshow not active. Stickers not enabled. Detection uses registry service start types, HKCU preference keys, and known Win11 behavioral changes.

### Diagnostic Tests Are Essential
The `Display_full_system_state` test that reads the real registry was the development turning point. It revealed handler identities (AnyCode = VSLauncher.exe), non-functional handlers (FileSyncEx), and tab routing bugs that were invisible from code analysis. See `docs/diagnostic-tests.md`.

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

### V2. Test MFS_HIDDEN on Ghost Handlers — PARTIALLY RESOLVED

**Update (2026-04-05):** Live testing confirmed FileSyncEx and WorkFolders produce no visible context menu entries on Win11. The MFS_HIDDEN theory was not directly tested, but the practical outcome is clear: these handlers are vestigial on Win11. FileSyncEx doesn't even affect overlay icons (those come from ShellIconOverlayIdentifiers). The app now marks these as inactive with explanations. The full MFS_HIDDEN probe investigation remains optional — the behavioral finding is sufficient for shipping.

**Original question:** Part 3 claims FileSyncEx, WorkFolders, and DesktopSlideshow insert menu items via `InsertMenuItem` but apply `MFS_HIDDEN` (`0x00000003`) to `MENUITEMINFO.fState`, making them invisible in Explorer while still detectable by a probe that only checks for item insertion. This is the proposed explanation for why the COM probe reports items added but nothing shows on screen.

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

### V4. Audit Static Verb Registry Paths -- COMPLETED 2026-03-09

**Status:** DONE. Automated via `docs/research/audit-static-verbs.ps1`. Full results at `docs/research/static-verb-registry-audit.md`.

**Scope exceeded original task:** Script scanned all 10 AC#1 scope paths (not just the 3 originally listed), covering 1507 file extensions and 6408 ProgIDs in addition to the 7 fixed scope paths.

**SUMMARY.md prediction results:**
- Visual Studio at `Directory\Background\shell\AnyCode` -- **Confirmed.** Also found at `Directory\shell\AnyCode`, `Directory\shell\Open with Code`, and `*\shell\VSCode`.
- WizTree dual registration -- **Exceeded.** Quad-registered: `Directory\shell`, `Folder\shell`, `Directory\Background\shell`, `Drive\shell`.
- Windows Terminal as PackagedCom only (no `shell\` entry) -- **Confirmed.** The `opennewtab` at `Folder\shell` is Explorer's native tab feature (CLSID `{11dbb47c-a525-400b-9e80-a54615a090c0}`), not Windows Terminal.

**Critical corrections to SUMMARY.md assumptions:**

1. **DelegateExecute location.** SUMMARY.md implies `DelegateExecute` is a value on the verb key itself. **Actual:** it lives inside the `command` subkey (`verb\command\DelegateExecute`). The scanner must read `command\DelegateExecute`, not `verb\DelegateExecute`.

2. **DelegateExecute prevalence.** SUMMARY.md treats DelegateExecute as an alternative to `command\(Default)`. **Actual:** 55% of all static verbs (43 of 78) use DelegateExecute. It is the dominant execution mechanism for Windows system verbs, not an edge case. 13 verbs have BOTH a command line and DelegateExecute (Shell prefers DelegateExecute when present).

3. **DropTarget execution mechanism.** Not mentioned in SUMMARY.md. `removeproperties` at `HKCR\*\shell` uses a `DropTarget` subkey instead of `command` or `DelegateExecute`. This is a third execution mechanism the scanner must detect.

4. **Shell-internal verbs.** `.SpotlightLearnMore`, `.SpotlightNextImage`, `EditStickers` at `DesktopBackground\shell` have no `command` subkey, no `DelegateExecute`, no `SubCommands`, no `DropTarget` -- zero execution data. These are dispatched via undocumented Shell internals.

5. **Empty scopes.** `.ext\shell` (0 of 1507 extensions) and `<ProgID>\shell` (0 of 6408 ProgIDs) are completely empty on modern Win11 26200. The scanner must still check them for completeness but should not expect results.

**78 total verbs found:** 41 Command-only, 30 DelegateExecute-only, 13 Both, 2 Cascading, 5 Unknown. 2 LegacyDisabled, 10 Extended, 4 ProgrammaticAccessOnly, 2 HasLUAShield.

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
