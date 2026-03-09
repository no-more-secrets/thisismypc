# ThisIsMyPC — Context Menu Real Data Catalog

**Source:** a desktop workstation (Windows 11 Pro)
**Date Collected:** March 8, 2026

---

## File: PNG (Image)

### Section 1
- Open
- Edit with Photos
- Convert to Adobe PDF
- Create with Designer
- Ask Copilot
- Edit with Paint
- Set as desktop background
- Print
- WinRAR →
- Move to OneDrive
- Edit with Notepad++
- Unlock with File Locksmith
- Resize with Image Resizer
- Edit in Notepad
- Rename with PowerRename
- Add to Favorites
- Open with Code

### Section 2
- Resize with Image Resizer
- Rotate right
- Rotate left

### Section 3
- Cast to Device →
- 7-Zip →
- Scan with Microsoft Defender...
- Open with

### Section 4
- Give access to →
- Copy as path
- Change Attributes
- Change Attributes / Use Saved

### Section 5
- Unlock with File Locksmith
- Share
- Restore previous versions
- Send to →

### Section 6
- Cut
- Copy

### Section 7
- Create shortcut
- Delete
- Rename

### Section 8
- Properties

**Total items: 35**

---

## File: HTML

### Section 1
- Open
- Convert to Adobe PDF
- WinRAR →
- Move to OneDrive
- Edit with Notepad++
- Unlock with File Locksmith
- Edit in Notepad
- Rename with PowerRename
- Add to Favorites
- Open with Code
- 7-Zip →
- Scan with Microsoft Defender...
- Open with

### Section 2
- Give access to →
- Copy as path
- Change Attributes
- Change Attributes / Use Saved

### Section 3
- Unlock with File Locksmith
- Share
- Restore previous versions
- Send to →

### Section 4
- Cut
- Copy

### Section 5
- Create shortcut
- Delete
- Rename

### Section 6
- Properties

**Total items: 26**

---

## File: PDF

### Section 1
- Open
- Ask Copilot
- Edit with Adobe Acrobat
- WinRAR →
- Move to OneDrive
- Edit with Notepad++
- Unlock with File Locksmith
- Edit in Notepad
- Rename with PowerRename
- Add to Favorites
- Open with Code
- 7-Zip →
- Scan with Microsoft Defender...
- Open with

### Section 2
- Give access to →
- Copy as path
- Change Attributes
- Change Attributes / Use Saved

### Section 3
- Unlock with File Locksmith
- Share
- Restore previous versions
- Send to →

### Section 4
- Cut
- Copy

### Section 5
- Create shortcut
- Delete
- Rename

### Section 6
- Properties

**Total items: 27**

---

## Folder (Direct Right-Click)

### Section 1
- Open
- Open in new tab
- Open in new window
- Pin to Quick access
- WizTree
- WinRAR →
- Unlock with File Locksmith
- Open in Terminal
- Rename with PowerRename
- Open with Visual Studio
- Open with VS Code
- 7-Zip →
- Scan with Microsoft Defender...

### Section 2
- Give access to →
- Restore previous versions

### Section 3
- Combine files in Acrobat...

### Section 4
- Include in library →
- Pin to Start
- Copy as path
- Change Attributes
- Change Attributes / Use Saved

### Section 5
- Unlock with File Locksmith
- Send to →

### Section 6
- Cut
- Copy

### Section 7
- Create shortcut
- Delete
- Rename

### Section 8
- Properties

**Total items: 30**

---

## Folder Background (Inside Folder)

### Section 1
- View →
- Sort by →
- Group by →
- Refresh

### Section 2
- Customize this folder...

### Section 3
- Paste
- Undo Move (Ctrl+Z)

### Section 4
- Open in Terminal
- Rename with PowerRename
- Open with Visual Studio
- WizTree

### Section 5
- Give access to →

### Section 6
- New →

### Section 7
- Properties

**Total items: 15**

---

## Desktop Background

### Section 1
- View →
- Sort by →
- Refresh

### Section 2
- Paste
- Undo Rename (Ctrl+Z) [any undo/redo appears here, if no recent action, nothing appears]

### Section 3 (visually unified with Section 2)
- Open in Terminal
- Open with Visual Studio
- WizTree

### Section 4
- NVIDIA App →

### Section 5
- NVIDIA Control Panel

### Section 6
- New →

### Section 7
- Display settings
- Personalize

**Total items: 13**

---

## Drag-Right-Click (Drag to New Location)

Triggered by holding right-click on a file or folder and dragging it to a different folder. This is a distinct invocation path from the standard right-click menu.

### Section 1
- 7-Zip →
- WinRAR →

### Section 2
- Copy here
- Move here
- Create shortcuts here

### Section 3
- Cancel

**Total items: 6**

**Notes:**
- "Move here" is only present when dragging into a different folder. Dragging to empty space within the same folder omits it (since the operation would be a no-op).
- This menu is minimal by design — it only surfaces operations relevant to the drag-drop action.
- 7-Zip and WinRAR register here as well, likely via `HKCR\Directory\shellex\DragDropHandlers` or the equivalent global drag-drop handler keys.

---

## File: PNG (Image) — Inside OneDrive Synced Folder

Same as the standard PNG context menu, but with an additional OneDrive-injected section. Only appears when the file resides within a OneDrive-synced directory and the account is actively connected.

### OneDrive-Injected Section (appears after Section 1)
- Share
- Copy Link
- Manage access
- View online
- Version history

These replace the standard "Move to OneDrive" entry (since the file is already in OneDrive).

**Total items: ~39** (standard 35 − Move to OneDrive + 5 OneDrive entries)

---

## Folder — Inside OneDrive Synced Folder

Same as the standard folder context menu, but with an additional OneDrive-injected section. Only appears when the folder resides within a OneDrive-synced directory and the account is actively connected.

### OneDrive-Injected Section (appears after Section 1)
- Share
- Copy Link
- Manage access
- View online
- Folder color

**Notes:**
- "Folder color" is OneDrive-specific — it does not appear on non-synced folders. This is a server-side metadata feature, not a Windows shell attribute.
- "Version history" (present on files) is replaced by "Folder color" (present on folders).
- Image 2 also shows "Add to Windows Media Player Legacy list" and "Play with Windows Media Player Legacy" which are WMP legacy handlers registered on folders — these may be present on all folder menus but were not captured in the original catalog.

**Total items: ~34** (standard 30 − any OneDrive-redundant entries + 5 OneDrive entries)

---

## Analysis

### Global Entries (Present on All File Types: PNG, HTML, PDF)
These entries appear on every file right-click and are registered on `HKCR\*` or equivalent global keys:
- Open
- WinRAR →
- Move to OneDrive (absent when file is already inside OneDrive-synced folder — replaced by OneDrive section)
- Edit with Notepad++
- Unlock with File Locksmith (appears **twice** on every file type)
- Edit in Notepad
- Rename with PowerRename
- Add to Favorites
- Open with Code
- 7-Zip →
- Scan with Microsoft Defender...
- Open with
- Give access to →
- Copy as path
- Change Attributes
- Change Attributes / Use Saved
- Share
- Restore previous versions
- Send to →
- Cut / Copy
- Create shortcut / Delete / Rename
- Properties

### File-Type-Specific Entries

| Entry | PNG | HTML | PDF | Folder | Folder BG | Desktop BG | Drag-RClick | OneDrive File | OneDrive Folder |
|---|---|---|---|---|---|---|---|---|---|
| Edit with Photos | ✓ | | | | | | | ✓ | |
| Convert to Adobe PDF | ✓ | ✓ | | | | | | ✓ | |
| Create with Designer | ✓ | | | | | | | ✓ | |
| Ask Copilot | ✓ | | ✓ | | | | | ✓ | |
| Edit with Paint | ✓ | | | | | | | ✓ | |
| Set as desktop background | ✓ | | | | | | | ✓ | |
| Print | ✓ | | | | | | | ✓ | |
| Resize with Image Resizer | ✓ (x2) | | | | | | | ✓ (x2) | |
| Rotate right/left | ✓ | | | | | | | ✓ | |
| Cast to Device → | ✓ | | | | | | | ✓ | ✓ |
| Edit with Adobe Acrobat | | | ✓ | | | | | | |
| Combine files in Acrobat... | | | | ✓ | | | | | ✓ |
| Open in new tab/window | | | | ✓ | | | | | ✓ |
| Pin to Quick access | | | | ✓ | | | | | ✓ |
| WizTree | | | | ✓ | ✓ | ✓ | | | ✓ |
| Open in Terminal | | | | ✓ | ✓ | ✓ | | | ✓ |
| Open with Visual Studio | | | | ✓ | ✓ | ✓ | | | ✓ |
| Open with VS Code | | | | ✓ | | | | | ✓ |
| Include in library → | | | | ✓ | | | | | ✓ |
| Pin to Start | | | | ✓ | | | | | ✓ |
| NVIDIA App → | | | | | | ✓ | | | |
| NVIDIA Control Panel | | | | | | ✓ | | | |
| Display settings | | | | | | ✓ | | | |
| Personalize | | | | | | ✓ | | | |
| Copy here | | | | | | | ✓ | | |
| Move here | | | | | | | ✓ ¹ | | |
| Create shortcuts here | | | | | | | ✓ | | |
| Share (OneDrive) | | | | | | | | ✓ | ✓ |
| Copy Link | | | | | | | | ✓ | ✓ |
| Manage access | | | | | | | | ✓ | ✓ |
| View online | | | | | | | | ✓ | ✓ |
| Version history | | | | | | | | ✓ | |
| Folder color | | | | | | | | | ✓ |
| WMP Legacy (list/play) | | | | | | | | | ✓ |

¹ "Move here" only present when dragging to a different folder, not to empty space within the same folder.

### Vendor Footprint Summary

| Vendor | Entries | Contexts |
|---|---|---|
| **Adobe** | Convert to Adobe PDF, Edit with Adobe Acrobat, Combine files in Acrobat | Files (global), PDF (specific), Folders |
| **Microsoft (non-OS)** | Ask Copilot, Create with Designer, Move to OneDrive | Selective per file type |
| **Microsoft (OneDrive)** | Share, Copy Link, Manage access, View online, Version history, Folder color | OneDrive-synced files + folders only (context-injected) |
| **Microsoft (WMP Legacy)** | Add to WMP Legacy list, Play with WMP Legacy | Folders (possibly global, needs further testing) |
| **PowerToys** | Unlock with File Locksmith (x2!), Rename with PowerRename, Resize with Image Resizer (x2 on PNG) | Global + image-specific |
| **WinRAR** | WinRAR → | Global (files + folders + drag-right-click) |
| **7-Zip** | 7-Zip → | Global (files + folders + drag-right-click) |
| **WizTree** | WizTree | Folders + folder background + desktop background |
| **Notepad++** | Edit with Notepad++ | Global (files) |
| **VS Code** | Open with Code | Global (files) + folders |
| **Visual Studio** | Open with Visual Studio | Folders + folder background + desktop background |
| **NVIDIA** | NVIDIA App →, NVIDIA Control Panel | Desktop background (registered at Directory\Background, COM-probe filtered to desktop only) |

### Duplicate Entry Bug
- **File Locksmith** appears twice in every file context menu (Sections 1 and 5) and twice in the folder context menu (Sections 1 and 5). This is a PowerToys registration issue.
- **Image Resizer** appears twice on PNG files (Sections 1 and 2). Another PowerToys double-registration.

### Key Takeaways
1. The bottom half of every file context menu (Give access to → Properties) is essentially identical regardless of file type — this is the "persistent core."
2. Adobe registers different handlers per file type rather than one global entry, making it harder to clean up with a single toggle.
3. PowerToys is the worst offender for duplicate entries, registering File Locksmith in two different handler locations.
4. Background context menus (folder and desktop) are significantly cleaner than file or folder menus — fewer apps register background handlers.
5. The global `*` registrations (WinRAR, 7-Zip, Notepad++, etc.) are the primary bloat contributors since they appear on every single file type.
6. The desktop background menu is the leanest visual context (13 items). NVIDIA registers its shell extension at `Directory\Background` (not `DesktopBackground`), but the COM handler filters itself to only inject items on the desktop surface — making it appear desktop-exclusive despite the shared registration path. Similarly, PowerRename and several other handlers are registered at `Directory\Background` but their COM probes exclude them from the desktop, so the desktop lacks Customize, Rename with PowerRename, and Give access to.
7. **Drag-right-click is a distinct invocation path** with its own minimal menu (6 items). Only compression vendors (7-Zip, WinRAR) register DragDropHandlers here, plus the OS-native Copy/Move/Shortcut operations. "Move here" is conditionally omitted when the drag target is empty space in the same folder.
8. **OneDrive injects a context-dependent section** into file and folder menus when the item lives in a synced folder. This replaces "Move to OneDrive" with 5 cloud-specific entries (Share, Copy Link, Manage access, View online, + Version history for files or Folder color for folders). ThisIsMyPC must detect OneDrive sync state to accurately catalog entries.
9. **WMP Legacy handlers persist on folders** even on modern Windows 11 installs — "Add to Windows Media Player Legacy list" and "Play with Windows Media Player Legacy" appear in the folder legacy menu. These are likely orphaned registrations on many systems where WMP is not actively used.

### Scanner Cross-Reference (March 9, 2026)

Scanner output: 62 unique handlers (25 COM, 37 static verbs). Tab counts: File (22), Folder (26), Folder Background (12), Desktop (18), Misc (18).

**Catalog → Scanner handler mapping:**
| Catalog Entry | Scanner Handler | Type | Notes |
|---|---|---|---|
| Edit with Notepad++ | `Notepad++ Context menu` (ANotepad++64) | Static Verb | Display name from verb key default value |
| Add to Favorites | `@shell32.dll,-51608` (pintohomefile) | Static Verb | Indirect string, unresolved |
| Open with Code (files) | `Open with Code` (VSCode) | Static Verb | Display name from default value "Open w&ith Code" |
| Open with VS Code (folders) | `Open with VS Code` (Open with Code) | Static Verb | Display name from default value |
| Open with Visual Studio | `AnyCode` | Static Verb | Default value is indirect string `@VSLauncherUI.dll,-1002`, unresolved |
| 7-Zip → | `7-Zip` | COM Handler | Currently disabled (blocked list + dash-prefix) |
| Scan with Microsoft Defender... | `EPP` | COM Handler | Windows Defender shell extension |
| Open with | `Open With` | COM Handler | Critical — `{09799AFB-AD67-11d1-ABCD-00C04FC30936}` |
| Give access to → | `Sharing` | COM Handler | Critical — `{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}` |
| Copy as path | `CopyAsPathMenu` | COM Handler | Critical — `{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}` |
| Change Attributes | `ACShell` | COM Handler | Romain Petges — File tab only |
| Share | `ModernSharing` | COM Handler | Modern Win11 share dialog |
| Send to → | `SendTo` | COM Handler | Critical — `{7BA4C740-9E81-11CF-99D3-00AA004AE837}` |
| Unlock with File Locksmith | `FileLocksmithExt` | COM Handler | PowerToys — File + Misc tabs |
| Rename with PowerRename | `PowerRenameExt` | COM Handler | PowerToys — File + Background + Desktop tabs |
| Pin to Start | `PintoStartScreen` | COM Handler | Folder tab only |
| Pin to taskbar | `Taskband Pin` | COM Handler | Critical — `{90AA3A4E-1CBA-4233-B8BB-535773D48449}`, inverted CLSID registration |
| Pin to Start (files) | `Start Menu Pin` | COM Handler | Critical — `{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}`, inverted CLSID registration |
| NVIDIA App → | `NvAppDesktopContext` | COM Handler | Registered at Directory\Background, COM-probe filtered |
| NVIDIA Control Panel | `NvCplDesktopContext` | COM Handler | Registered at Directory\Background, COM-probe filtered |
| WizTree | `WizTree` | Static Verb | Folders + Background + Desktop + Misc |
| New → | `New` | COM Handler | Critical — `{D969A300-E7FF-11d0-A93B-00A0C90F2719}` |

**Handlers found by scanner but not in catalog** (hidden, conditional, or Shift-only):
- `Taskband Pin`, `Start Menu Pin` — inverted CLSID registrations, conditionally visible per file type
- `WorkFolders`, `FileSyncEx` — background COM handlers that inject items conditionally (e.g., OneDrive)
- `DesktopSlideshow` — only visible when desktop slideshow is active
- `.SpotlightLearnMore`, `.SpotlightNextImage` — Windows Spotlight entries, conditional on wallpaper source
- `EditStickers` — Win11 desktop sticker feature
- `cmd`, `Powershell`, `WSL` — Shift-only (Extended) entries, not visible in normal right-click
- `removeproperties`, `WorkfoldersControl`, `OfflineFilesLaunchSyncCenter` — ProgrammaticAccessOnly (hidden)
- `Open With EncryptionMenu` — EFS encryption variant of Open With
- `EnhancedStorageShell` — hardware encryption (Misc/Drive tab)
- BitLocker verbs (`encrypt-bde`, `manage-bde`, etc.) — Drive-specific, Misc tab

**Entries in catalog not tracked by scanner** (out of scope for current scanner):
- Shell built-ins: View, Sort by, Group by, Refresh, Paste, Undo, Cut, Copy, Delete, Rename, Properties, Create shortcut, Customize this folder
- Modern/IExplorerCommand handlers: Open in Terminal, Edit in Notepad, Ask Copilot, Create with Designer
- File-type-specific verbs: Edit with Photos, Edit with Paint, Set as desktop background, Print, Rotate, Cast to Device
- DragDropHandlers: 7-Zip drag, WinRAR drag, Copy/Move/Shortcut here
- OneDrive injected items: Share, Copy Link, Manage access, View online, Version history, Folder color
