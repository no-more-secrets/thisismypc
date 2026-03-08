# Context Menu Handler Analysis

**Source:** ShellExView export (`Shell_Extensions_List.html`) from Windows 11 25H2
**Analyzed:** 2026-03-07
**Purpose:** Inform Story 2.2 (Context Menu Handler Management) implementation

---

## 1. Shell Extension Type Distribution

The ShellExView export contains **299 total shell extensions** across **21 distinct types**. Story 2.2 manages only Context Menu handlers. The full distribution is provided so the implementation knows exactly what to skip.

| Type | Count | In Scope for 2.2? |
|---|---|---|
| Shell Folder | 70 | No |
| **Context Menu** | **48** | **Yes** |
| Property Handler | 32 | No |
| Preview Handler | 18 | No |
| Disk Cleanup Handler | 17 | No |
| Property Sheet | 17 | No |
| Thumbnail Handler | 15 | No |
| Icon Handler | 12 | No |
| Icon Overlay Handler | 12 | No |
| Thumbnail | 10 | No |
| Drop Handler | 10 | No |
| MetaData | 7 | No |
| InfoTip Handler | 6 | No |
| Drag & Drop Handler | 5 | No |
| Shell Link (Unicode) | 5 | No |
| System | 5 | No |
| Shell Link (ANSI) | 3 | No |
| Copy Hook Handler | 2 | No |
| IE Toolbar | 1 | No |
| URL Search Hook | 1 | No |
| Search Handler | 1 | No |

**Filtering rule for Story 2.2:** When enumerating `shellex\ContextMenuHandlers` subkeys, the handler type is implicit from the registry path. ShellExView distinguishes types by the parent key name (e.g., `ContextMenuHandlers` vs `PropertySheetHandlers` vs `IconHandler`). The implementation should only enumerate subkeys under `ContextMenuHandlers` paths, which naturally filters to the 48 in-scope handlers.

---

## 2. Context Menu Handler Inventory

### Summary

- **Total context menu handlers:** 48
- **Microsoft (Windows built-in):** 40 (83%)
- **Third-party:** 8 (17%)
- **Currently enabled:** 43
- **Currently disabled:** 5

### Third-Party Context Menu Handlers (Primary Management Targets)

These are the handlers users are most likely to want to enable/disable. All 8 are safe to disable without affecting Windows core functionality.

| Extension Name | CLSID | DLL Path | Company | Version | Enabled | File Extensions / Registration |
|---|---|---|---|---|---|---|
| 7-Zip Shell Extension | `{23170F69-40C1-278A-1000-000100020000}` | `C:\Program Files\7-Zip\7-zip.dll` | Igor Pavlov | 25.01 | Yes | `*`, Directory, Folder |
| Acrobat Elements Context Menu | `{A6595CD1-BF77-430A-A452-18696685F7C7}` | `C:\Program Files\Adobe\Acrobat DC\Acrobat Elements\ContextMenuShim64.dll` | Adobe Systems Inc. | 25.1.20432.0 | Yes | Folder |
| Attribute Changer Shell Extension | `{D3F9A525-8824-497A-BE36-B23E22F141FC}` | `C:\Program Files\Attribute Changer\acshell.dll` | Romain Petges | 1140.2025.8.12 | Yes | AllFileSystemObjects |
| AccExt Class (Adobe Core Sync) | `{2A118EB5-5797-4F5E-8B3D-F4ECBA3C98E4}` | `C:\Program Files (x86)\Common Files\Adobe\CoreSyncExtension\CoreSync_x64.dll` | (none listed) | 7.8.10.1 | Yes | Folder |
| NVIDIA CPL Context Menu Extension | `{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}` | `C:\WINDOWS\System32\DriverStore\...\nvshext.dll` | NVIDIA Corporation | 581.57 | **No** | Directory\Background |
| NvAppShExt Class | `{A929C4CE-FD36-4270-B4F5-34ECAC5BD63C}` | `C:\WINDOWS\System32\DriverStore\...\nv3dappshext.dll` | NVIDIA Corporation | 6.14.15.8157 | **No** | .exe, .lnk, exefile, lnkfile |
| OpenGLShExt Class | `{E97DEC16-A50D-49bb-AE24-CF682282E08D}` | `C:\WINDOWS\System32\DriverStore\...\nv3dappshext.dll` | NVIDIA Corporation | 6.14.15.8157 | **No** | .bat, .cmd, .com, .exe, and many more ProgIDs |
| DesktopContext Class (NVIDIA App) | `{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}` | `C:\Program Files\NVIDIA Corporation\NVIDIA App\NvCpl\nvui.dll` | NVIDIA Corporation | 8.17.15.5527 | Yes | Directory\Background |

**Notes:**
- The 3 disabled NVIDIA handlers were likely disabled by the user via ShellExView or NVIDIA settings. NvAppShExt and OpenGLShExt are legacy handlers superseded by the newer DesktopContext Class (NVIDIA App).
- Adobe has 2 context menu handlers: Acrobat (folder-level "Convert to PDF") and Core Sync (Creative Cloud sync status).
- 7-Zip registers across `*`, Directory, and Folder, making it appear on virtually all right-click menus.

### Microsoft Context Menu Handlers (Windows Built-In)

These 40 handlers are integral to Windows functionality. The UI should label them as "Windows built-in" and warn before disabling.

#### System-Critical (DO NOT disable -- will break core Windows features)

| Extension Name | CLSID | DLL | Description | Registration |
|---|---|---|---|---|
| Open With Context Menu Handler | `{09799AFB-AD67-11d1-ABCD-00C04FC30936}` | shell32.dll | "Open with..." menu | `*` (all files) |
| Microsoft SendTo Service | `{7BA4C740-9E81-11CF-99D3-00AA004AE837}` | shell32.dll | "Send to" submenu | AllFileSystemObjects |
| New Menu Handler | `{D969A300-E7FF-11d0-A93B-00A0C90F2719}` | shell32.dll | "New >" submenu in background | Directory\Background |
| Copy as Path Menu | `{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}` | shell32.dll | "Copy as path" | AllFileSystemObjects |
| Shortcut | `{00021401-0000-0000-C000-000000000046}` | windows.storage.dll | .lnk shortcut handling | .lnk, lnkfile |
| Shortcut (.symlink) | `{85cfccaf-2d14-42b6-80b6-f40f65d016e7}` | windows.storage.dll | .symlink handling | .symlink |
| Shell extensions for sharing | `{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}` | ntshrui.dll | Network sharing | `*`, Directory, Drive |
| Start Menu Pin | `{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}` | shell32.dll | "Pin to Start" | `*`, AllFileSystemObjects, Folder |
| Taskband Pin | `{90AA3A4E-1CBA-4233-B8BB-535773D48449}` | shell32.dll | "Pin to taskbar" | `*` |
| Encryption Context Menu | `{A470F8CF-A1E8-4f65-8335-227475AA5C46}` | shell32.dll | EFS encrypt/decrypt | `*`, Directory |

#### Standard Windows Features (safe to disable but reduces functionality)

| Extension Name | CLSID | DLL | Description | Registration |
|---|---|---|---|---|
| Pin To Start Screen verb handler | `{470C0EBD-5D73-4d58-9CED-E91E22E23282}` | appresolver.dll | Pin to Start | .exe, Folder |
| Play To menu | `{7AD84985-87B4-4a16-BE58-8B72A5B390F7}` | playtomenu.dll | "Cast to Device" for media files | Media file types |
| Client Side Caching UI | `{474C98EE-CF3D-41f5-80E3-4AAB0AB04301}` | cscui.dll | Offline files sync | AllFileSystemObjects, Directory, Folder |
| CompatContextMenu Class | `{1d27f844-3a1f-4410-85ac-14651078412d}` | acppage.dll | "Troubleshoot compatibility" | .exe, .bat, .cmd, .msi |
| Compressed (zipped) Folder Context Menu | `{b8cdcb65-b1bf-4b42-9428-1dfdb7ee92af}` | zipfldr.dll | "Extract All..." for .zip | CompressedFolder, .zip |
| Compressed Archive Folder Context Menu | `{EE07CEF5-3441-4CFB-870A-4002C724783A}` | zipfldr.dll | Extract for .7z, .rar, .tar, etc. | Archive formats |
| CryptPKO Class | `{7444C717-39BF-11D1-8CD9-00C04FC29D45}` | cryptext.dll | Certificate file handling | .pko |
| Internet Shortcut | `{FBF23B40-E3F0-101B-8488-00AA003E56F8}` | ieframe.dll | .URL file context menu | .URL (currently disabled) |
| Work Folders Context Menu Handler | `{E61BF828-5E63-4287-BEF1-60B1A4FDE0E3}` | WorkfoldersShell.dll | Work Folders sync | Directory\Background, `*`, Directory |
| .contact shell context menu | `{CF67796C-F57F-45f8-92FB-AD698826C602}` | wab32.dll | Windows Contacts | contact_wab_auto_file |
| .group shell context menu | `{16C2C29D-0E5F-45f3-A445-03E03F587B7D}` | wab32.dll | Windows Contacts groups | group_wab_auto_file |
| FileSyncEx (OneDrive) | `{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}` | FileSyncShell64.dll | OneDrive sync status | Directory\Background, `*`, Directory |
| EPP (Windows Defender) | `{09A47860-11B0-4DA5-AFA5-26D86198A780}` | shellext.dll | "Scan with Microsoft Defender" | `*`, Directory, Drive |
| Microsoft OLE DB Data Links | `{2206CDB2-19C1-11D1-89E0-00C04FD7A829}` | oledb32.dll | .UDL file handling | .UDL |
| Portable Devices Menu | `{D6791A63-E7E2-4fee-BF52-5DED8E86E9B8}` | wpdshext.dll | Portable device menus | Drive |
| Previous Versions Property Page | `{596AB062-B4D2-4215-9F74-E9109B0A8153}` | twext.dll | "Restore previous versions" | AllFileSystemObjects, Directory, Drive |
| PrintUIShellExtension Class | `{77597368-7b15-11d0-a0c2-080036af3f03}` | printui.dll | Printer context menu | Printers |
| Ribbon Modern Share Verb | `{e2bf9676-5f8f-435c-97eb-11607a5bedf7}` | ntshrui.dll | Share dialog | AllFileSystemObjects |
| SlideshowContextMenu | `{0bf754aa-c967-445c-ab3d-d8fda9bae7ef}` | stobject.dll | Desktop slideshow | DesktopBackground |
| Enhanced Storage Context Menu Handler | `{2854F705-3548-414C-A113-93E27C808C85}` | EhStorShell.dll | Enhanced storage (encrypted USB) | Drive |
| Microsoft Windows Font Context Menu Handler | `{1a184871-359e-4f67-aad9-5b9905d62232}` | fontext.dll | "Install" / "Preview" for fonts | .fon, .otf, .ttf, .ttc, .pfm |
| Windows Photo Viewer Image Verbs | `{FFE2A43C-56B9-4bf5-9A79-CC6D4285608A}` | PhotoViewer.dll | Image file verbs | Image formats |
| Include In Library Sub Context Menu | `{3dad6c5d-2167-4cae-9914-f99e41c12cfa}` | shell32.dll | "Include in library" | Folder |
| Library Folder Context Menu | `{0af96ede-aebf-41ed-a1c8-cf7a685505b6}` | shell32.dll | Library folder operations | .library-ms, LibraryFolder |
| Open Containing Folder Menu | `{37ea3a21-7493-4208-a011-7f9ea79ce9f5}` | shell32.dll | "Open file location" | .lnk, .symlink |
| OpenSearch Result Context Menu | `{F9A7AB61-C0BC-490e-A7FE-BFF26B327A3F}` | shell32.dll | OpenSearch provider results | OpenSearchProvider |
| ShellFolder for CD Burning | `{fbeb8a05-beee-4442-804e-409d6c4515e9}` | shell32.dll | CD/DVD burning | Drive |

#### PowerToys Handlers (Microsoft-signed but optional)

These are installed via Microsoft PowerToys. They show as `Microsoft=Yes` because they are Microsoft-signed, but they are optional user-installed tools and behave more like third-party handlers from a management perspective.

| Extension Name | CLSID | DLL | Description | Enabled | Registration |
|---|---|---|---|---|---|
| File Locksmith Shell Extension | `{84D68575-E186-46AD-B0CB-BAEB45EE29C0}` | PowerToys.FileLocksmithExt.dll | Show processes locking a file | Yes | AllFileSystemObjects, Drive |
| ImageResizer Shell Extension | `{51B4D7E5-7568-4234-B4BB-47FB3C016A69}` | PowerToys.ImageResizerExt.dll | Resize images from context menu | Yes | Image file types |
| PowerRename Shell Extension | `{0440049F-D1DC-4E46-B27B-98393D79486B}` | PowerToys.PowerRenameExt.dll | Batch rename files | **No** | Directory\Background, AllFileSystemObjects |

---

## 3. Microsoft vs Third-Party Classification

### Classification Rules for Implementation

The ShellExView `Microsoft` column (Yes/No) provides a reliable starting signal, but the implementation should refine it:

1. **Check the `Microsoft` flag** from the DLL's version info resource (equivalent to ShellExView's column).
2. **Check the DLL path** -- files under `C:\Windows\System32\` or `C:\Windows\system32\` are almost certainly Windows built-in.
3. **PowerToys special case** -- DLLs under `C:\Program Files\PowerToys\` are Microsoft-signed but user-installed optional tools. Consider categorizing these as "Microsoft (Optional)" in the UI so users feel comfortable disabling them.
4. **OneDrive** -- `FileSyncShell64.dll` is Microsoft-signed and ships with Windows but is effectively a bundled app. It appears under `Microsoft=Yes`.

### UX Treatment Recommendations

| Category | Label | Warning on Disable | Examples |
|---|---|---|---|
| Windows System (Critical) | "Windows built-in (system)" | Block or strong warning | Open With, SendTo, New Menu, Shortcut |
| Windows System (Feature) | "Windows built-in" | Mild warning | Play To, Compatibility, Previous Versions |
| Microsoft Optional | "Microsoft (optional)" | No warning | PowerToys handlers |
| Third-party | (company name) | No warning | 7-Zip, Adobe, NVIDIA, Attribute Changer |

---

## 4. Cross-Reference Against Documented Enumeration Paths

### Documented Paths (from epic2-registry-research.md)

| # | Registry Path | ShellExView Token | Handlers Found |
|---|---|---|---|
| 1 | `HKCR\*\shellex\ContextMenuHandlers\` | `*` | 7-Zip, Work Folders, FileSyncEx, EPP, Open With, Encryption, Shell extensions for sharing, Start Menu Pin, Taskband Pin |
| 2 | `HKCR\AllFilesystemObjects\shellex\ContextMenuHandlers\` | `AllFileSystemObjects` | Attribute Changer, Client Side Caching UI, File Locksmith, PowerRename, Previous Versions, Ribbon Modern Share, Copy as Path, SendTo, Start Menu Pin |
| 3 | `HKCR\Directory\shellex\ContextMenuHandlers\` | `Directory` | 7-Zip, Client Side Caching UI, Work Folders, FileSyncEx, EPP, Previous Versions, Shell extensions for sharing, Encryption |
| 4 | `HKCR\Directory\Background\shellex\ContextMenuHandlers\` | `Directory\Background` | Work Folders, FileSyncEx, NVIDIA CPL, DesktopContext (NVIDIA), PowerRename, Shell extensions for sharing, New Menu Handler |
| 5 | `HKCR\Folder\shellex\ContextMenuHandlers\` | `Folder` | 7-Zip, Acrobat, Pin To Start, Client Side Caching UI, AccExt (Core Sync), Include In Library, Start Menu Pin |
| 6 | `HKCR\<ProgID>\shellex\ContextMenuHandlers\` | Per-file-type ProgIDs | CompatContextMenu (exefile), Compressed (CompressedFolder), CryptPKO (PKOFile), NvAppShExt (exefile), Font Handler (ttffile, otffile), etc. |

**Result: All 48 context menu handlers map to the 6 documented enumeration path categories.** No handlers were found in completely unexpected locations.

### Additional Registration Locations (Beyond the 6 Documented)

Several Microsoft handlers also register under specialized virtual shell classes. These are technically ProgID-based registrations (covered by path #6 above) but target virtual shell objects rather than file types:

| Location | HKCR Path | Handlers |
|---|---|---|
| Drive | `HKCR\Drive\shellex\ContextMenuHandlers\` | EPP (Defender), Portable Devices, File Locksmith, Previous Versions, Sharing, Enhanced Storage, CD Burning |
| DesktopBackground | `HKCR\DesktopBackground\shellex\ContextMenuHandlers\` | SlideshowContextMenu |
| Printers | `HKCR\Printers\shellex\ContextMenuHandlers\` | PrintUIShellExtension |
| LibraryFolder | `HKCR\LibraryFolder\shellex\ContextMenuHandlers\` | Library Folder Context Menu |
| LibraryFolder\background | `HKCR\LibraryFolder\background\shellex\ContextMenuHandlers\` | Shell extensions for sharing, New Menu Handler |
| UserLibraryFolder | `HKCR\UserLibraryFolder\shellex\ContextMenuHandlers\` | Shell extensions for sharing, SendTo |
| OpenSearchProvider | `HKCR\OpenSearchProvider\shellex\ContextMenuHandlers\` | OpenSearch Result Context Menu |

**Impact on Story 2.2:** These are all Microsoft system handlers for niche shell objects. No third-party handler uses any of these locations. The implementation does not need to enumerate these paths for the core context menu management feature. If comprehensive enumeration is desired later, `Drive` is the most useful addition (7 handlers).

---

## 5. Safe-to-Disable vs Risky Classification

### NEVER Disable (System-Critical)

Disabling these will break fundamental Windows Explorer operations. The UI should either hide the disable button or show a blocking confirmation dialog.

| Handler | Why Critical |
|---|---|
| Open With Context Menu Handler | Removes "Open with..." from all files |
| Microsoft SendTo Service | Removes "Send to" submenu |
| New Menu Handler | Removes "New >" from desktop/folder background |
| Shortcut (both .lnk and .symlink) | Breaks shortcut file handling |
| Copy as Path Menu | Removes "Copy as path" (Win11 feature) |
| Start Menu Pin | Removes "Pin to Start" |
| Taskband Pin | Removes "Pin to taskbar" |
| Shell extensions for sharing | Removes network sharing options |

### Warn Before Disabling (Windows Features)

These provide useful Windows features. Disabling them degrades functionality but does not break Explorer.

| Handler | Impact of Disabling |
|---|---|
| Pin To Start Screen verb handler | Removes secondary "Pin to Start" mechanism |
| Play To menu | Removes "Cast to Device" from media files |
| EPP (Windows Defender) | Removes "Scan with Microsoft Defender" right-click |
| FileSyncEx (OneDrive) | Removes OneDrive sync status icons and context menu |
| Previous Versions Property Page | Removes "Restore previous versions" option |
| Compressed (zipped) Folder Context Menu | Removes "Extract All..." for .zip files |
| Compressed Archive Folder Context Menu | Removes "Extract All..." for .7z, .rar, .tar, etc. |
| Encryption Context Menu | Removes EFS encrypt/decrypt options |
| CompatContextMenu Class | Removes "Troubleshoot compatibility" for executables |
| Windows Photo Viewer Image Verbs | Removes Photo Viewer context menu for images |
| Microsoft Windows Font Context Menu Handler | Removes "Install" / "Preview" for font files |

### Safe to Disable (Third-Party and Optional)

These are the primary targets for Story 2.2. Users commonly disable these to declutter the context menu.

| Handler | What Disappears | Notes |
|---|---|---|
| 7-Zip Shell Extension | 7-Zip submenu ("Extract here", "Add to archive", etc.) | Very common disable target |
| Acrobat Elements Context Menu | "Convert to Adobe PDF" on folders | Common disable target |
| AccExt Class (Adobe Core Sync) | Creative Cloud sync status on folders | |
| Attribute Changer Shell Extension | "Change Attributes" on all files/folders | |
| NVIDIA CPL Context Menu Extension | "NVIDIA Control Panel" on desktop background | Already disabled on this system |
| NvAppShExt Class | NVIDIA app profile for executables | Already disabled; legacy |
| OpenGLShExt Class | NVIDIA OpenGL settings for executables | Already disabled; legacy |
| DesktopContext Class (NVIDIA App) | "NVIDIA App" on desktop background | Replacement for NVIDIA CPL |
| File Locksmith Shell Extension | "What's using this file?" (PowerToys) | |
| ImageResizer Shell Extension | "Resize pictures" (PowerToys) | |
| PowerRename Shell Extension | "PowerRename" (PowerToys) | Already disabled on this system |

---

## 6. Structured Reference Table

Complete table of all 48 context menu handlers, sorted by classification, ready for story implementation.

### Legend

- **Class:** `CRITICAL` = never disable, `SYSTEM` = warn before disabling, `OPTIONAL` = Microsoft optional (PowerToys), `3P` = third-party (safe)
- **Enabled:** Current state on analyzed system
- **Scope:** Primary HKCR registration targets (simplified)

| # | Extension Name | CLSID | DLL | Company | Class | Enabled | Scope |
|---|---|---|---|---|---|---|---|
| 1 | Open With Context Menu Handler | `{09799AFB-AD67-11d1-ABCD-00C04FC30936}` | shell32.dll | Microsoft | CRITICAL | Yes | `*` |
| 2 | Microsoft SendTo Service | `{7BA4C740-9E81-11CF-99D3-00AA004AE837}` | shell32.dll | Microsoft | CRITICAL | Yes | AllFileSystemObjects |
| 3 | New Menu Handler | `{D969A300-E7FF-11d0-A93B-00A0C90F2719}` | shell32.dll | Microsoft | CRITICAL | Yes | Directory\Background |
| 4 | Copy as Path Menu | `{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}` | shell32.dll | Microsoft | CRITICAL | Yes | AllFileSystemObjects |
| 5 | Shortcut | `{00021401-0000-0000-C000-000000000046}` | windows.storage.dll | Microsoft | CRITICAL | Yes | .lnk |
| 6 | Shortcut (.symlink) | `{85cfccaf-2d14-42b6-80b6-f40f65d016e7}` | windows.storage.dll | Microsoft | CRITICAL | Yes | .symlink |
| 7 | Shell extensions for sharing | `{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}` | ntshrui.dll | Microsoft | CRITICAL | Yes | `*`, Directory, Drive |
| 8 | Start Menu Pin | `{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}` | shell32.dll | Microsoft | CRITICAL | Yes | `*`, AllFileSystemObjects, Folder |
| 9 | Taskband Pin | `{90AA3A4E-1CBA-4233-B8BB-535773D48449}` | shell32.dll | Microsoft | CRITICAL | Yes | `*` |
| 10 | Encryption Context Menu | `{A470F8CF-A1E8-4f65-8335-227475AA5C46}` | shell32.dll | Microsoft | CRITICAL | Yes | `*`, Directory |
| 11 | Pin To Start Screen verb handler | `{470C0EBD-5D73-4d58-9CED-E91E22E23282}` | appresolver.dll | Microsoft | SYSTEM | Yes | .exe, Folder |
| 12 | Play To menu | `{7AD84985-87B4-4a16-BE58-8B72A5B390F7}` | playtomenu.dll | Microsoft | SYSTEM | Yes | Media types |
| 13 | Client Side Caching UI | `{474C98EE-CF3D-41f5-80E3-4AAB0AB04301}` | cscui.dll | Microsoft | SYSTEM | Yes | AllFileSystemObjects, Directory, Folder |
| 14 | CompatContextMenu Class | `{1d27f844-3a1f-4410-85ac-14651078412d}` | acppage.dll | Microsoft | SYSTEM | Yes | .exe, .bat, .cmd, .msi |
| 15 | Compressed (zipped) Folder Context Menu | `{b8cdcb65-b1bf-4b42-9428-1dfdb7ee92af}` | zipfldr.dll | Microsoft | SYSTEM | Yes | .zip, CompressedFolder |
| 16 | Compressed Archive Folder Context Menu | `{EE07CEF5-3441-4CFB-870A-4002C724783A}` | zipfldr.dll | Microsoft | SYSTEM | Yes | .7z, .rar, .tar, .gz, etc. |
| 17 | CryptPKO Class | `{7444C717-39BF-11D1-8CD9-00C04FC29D45}` | cryptext.dll | Microsoft | SYSTEM | Yes | .pko |
| 18 | Internet Shortcut | `{FBF23B40-E3F0-101B-8488-00AA003E56F8}` | ieframe.dll | Microsoft | SYSTEM | No | .URL |
| 19 | Work Folders Context Menu Handler | `{E61BF828-5E63-4287-BEF1-60B1A4FDE0E3}` | WorkfoldersShell.dll | Microsoft | SYSTEM | Yes | Directory\Background, `*`, Directory |
| 20 | .contact shell context menu | `{CF67796C-F57F-45f8-92FB-AD698826C602}` | wab32.dll | Microsoft | SYSTEM | Yes | contact_wab_auto_file |
| 21 | .group shell context menu | `{16C2C29D-0E5F-45f3-A445-03E03F587B7D}` | wab32.dll | Microsoft | SYSTEM | Yes | group_wab_auto_file |
| 22 | FileSyncEx (OneDrive) | `{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}` | FileSyncShell64.dll | Microsoft | SYSTEM | Yes | Directory\Background, `*`, Directory |
| 23 | EPP (Windows Defender) | `{09A47860-11B0-4DA5-AFA5-26D86198A780}` | shellext.dll | Microsoft | SYSTEM | Yes | `*`, Directory, Drive |
| 24 | Microsoft OLE DB Data Links | `{2206CDB2-19C1-11D1-89E0-00C04FD7A829}` | oledb32.dll | Microsoft | SYSTEM | Yes | .UDL |
| 25 | Portable Devices Menu | `{D6791A63-E7E2-4fee-BF52-5DED8E86E9B8}` | wpdshext.dll | Microsoft | SYSTEM | Yes | Drive |
| 26 | Previous Versions Property Page | `{596AB062-B4D2-4215-9F74-E9109B0A8153}` | twext.dll | Microsoft | SYSTEM | Yes | AllFileSystemObjects, Directory, Drive |
| 27 | PrintUIShellExtension Class | `{77597368-7b15-11d0-a0c2-080036af3f03}` | printui.dll | Microsoft | SYSTEM | Yes | Printers |
| 28 | Ribbon Modern Share Verb | `{e2bf9676-5f8f-435c-97eb-11607a5bedf7}` | ntshrui.dll | Microsoft | SYSTEM | Yes | AllFileSystemObjects |
| 29 | SlideshowContextMenu | `{0bf754aa-c967-445c-ab3d-d8fda9bae7ef}` | stobject.dll | Microsoft | SYSTEM | Yes | DesktopBackground |
| 30 | Enhanced Storage Context Menu Handler | `{2854F705-3548-414C-A113-93E27C808C85}` | EhStorShell.dll | Microsoft | SYSTEM | Yes | Drive |
| 31 | Microsoft Windows Font Context Menu Handler | `{1a184871-359e-4f67-aad9-5b9905d62232}` | fontext.dll | Microsoft | SYSTEM | Yes | .fon, .otf, .ttf, .ttc, .pfm |
| 32 | Windows Photo Viewer Image Verbs | `{FFE2A43C-56B9-4bf5-9A79-CC6D4285608A}` | PhotoViewer.dll | Microsoft | SYSTEM | Yes | Image formats |
| 33 | Include In Library Sub Context Menu | `{3dad6c5d-2167-4cae-9914-f99e41c12cfa}` | shell32.dll | Microsoft | SYSTEM | Yes | Folder |
| 34 | Library Folder Context Menu | `{0af96ede-aebf-41ed-a1c8-cf7a685505b6}` | shell32.dll | Microsoft | SYSTEM | Yes | .library-ms, LibraryFolder |
| 35 | Open Containing Folder Menu | `{37ea3a21-7493-4208-a011-7f9ea79ce9f5}` | shell32.dll | Microsoft | SYSTEM | Yes | .lnk, .symlink |
| 36 | OpenSearch Result Context Menu | `{F9A7AB61-C0BC-490e-A7FE-BFF26B327A3F}` | shell32.dll | Microsoft | SYSTEM | Yes | OpenSearchProvider |
| 37 | ShellFolder for CD Burning | `{fbeb8a05-beee-4442-804e-409d6c4515e9}` | shell32.dll | Microsoft | SYSTEM | Yes | Drive |
| 38 | File Locksmith Shell Extension | `{84D68575-E186-46AD-B0CB-BAEB45EE29C0}` | PowerToys.FileLocksmithExt.dll | Microsoft (PowerToys) | OPTIONAL | Yes | AllFileSystemObjects, Drive |
| 39 | ImageResizer Shell Extension | `{51B4D7E5-7568-4234-B4BB-47FB3C016A69}` | PowerToys.ImageResizerExt.dll | Microsoft (PowerToys) | OPTIONAL | Yes | Image types |
| 40 | PowerRename Shell Extension | `{0440049F-D1DC-4E46-B27B-98393D79486B}` | PowerToys.PowerRenameExt.dll | Microsoft (PowerToys) | OPTIONAL | No | Directory\Background, AllFileSystemObjects |
| 41 | 7-Zip Shell Extension | `{23170F69-40C1-278A-1000-000100020000}` | 7-zip.dll | Igor Pavlov | 3P | Yes | `*`, Directory, Folder |
| 42 | Acrobat Elements Context Menu | `{A6595CD1-BF77-430A-A452-18696685F7C7}` | ContextMenuShim64.dll | Adobe Systems Inc. | 3P | Yes | Folder |
| 43 | AccExt Class (Adobe Core Sync) | `{2A118EB5-5797-4F5E-8B3D-F4ECBA3C98E4}` | CoreSync_x64.dll | Adobe | 3P | Yes | Folder |
| 44 | Attribute Changer Shell Extension | `{D3F9A525-8824-497A-BE36-B23E22F141FC}` | acshell.dll | Romain Petges | 3P | Yes | AllFileSystemObjects |
| 45 | NVIDIA CPL Context Menu Extension | `{3D1975AF-48C6-4f8e-A182-BE0E08FA86A9}` | nvshext.dll | NVIDIA Corporation | 3P | No | Directory\Background |
| 46 | NvAppShExt Class | `{A929C4CE-FD36-4270-B4F5-34ECAC5BD63C}` | nv3dappshext.dll | NVIDIA Corporation | 3P | No | .exe, .lnk |
| 47 | OpenGLShExt Class | `{E97DEC16-A50D-49bb-AE24-CF682282E08D}` | nv3dappshext.dll | NVIDIA Corporation | 3P | No | Executables, scripts |
| 48 | DesktopContext Class (NVIDIA App) | `{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}` | nvui.dll | NVIDIA Corporation | 3P | Yes | Directory\Background |

---

## Implementation Notes for Story 2.2

### Classification Heuristic (Runtime)

The app will not have this static table at runtime. It needs a heuristic to classify handlers discovered on any user's system:

```
1. Read DLL version info via FileVersionInfo.GetVersionInfo(dllPath)
2. If CompanyName contains "Microsoft" -> check path:
   a. If path contains "PowerToys" -> OPTIONAL
   b. If path starts with C:\Windows\ -> check against known CRITICAL CLSIDs
   c. Otherwise -> SYSTEM
3. Else -> 3P (third-party)
```

Ship the 10 CRITICAL CLSIDs as a hardcoded set. Everything else Microsoft-signed gets SYSTEM. This is conservative and safe.

### Known CRITICAL CLSIDs (Hardcode These)

```csharp
private static readonly HashSet<string> CriticalClsids = new(StringComparer.OrdinalIgnoreCase)
{
    "{09799AFB-AD67-11d1-ABCD-00C04FC30936}", // Open With
    "{7BA4C740-9E81-11CF-99D3-00AA004AE837}", // SendTo
    "{D969A300-E7FF-11d0-A93B-00A0C90F2719}", // New Menu
    "{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}", // Copy as Path
    "{00021401-0000-0000-C000-000000000046}", // Shortcut (.lnk)
    "{85cfccaf-2d14-42b6-80b6-f40f65d016e7}", // Shortcut (.symlink)
    "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}", // Sharing
    "{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}", // Start Menu Pin
    "{90AA3A4E-1CBA-4233-B8BB-535773D48449}", // Taskband Pin
    "{A470F8CF-A1E8-4f65-8335-227475AA5C46}", // Encryption
};
```

### Disable Mechanism Confirmation

ShellExView uses the dash-prefix approach documented in `epic2-registry-research.md` (Approach A). The 5 currently disabled handlers on this system (`Internet Shortcut`, `NVIDIA CPL`, `NvAppShExt`, `OpenGLShExt`, `PowerRename`) all use this method. This confirms the dash-prefix approach is the standard and works correctly on Windows 11 25H2.

### Enumeration Path Sufficiency

The 5 documented enumeration paths plus per-ProgID scanning cover **100% of third-party handlers** and all system handlers that users would want to manage. The additional virtual shell class locations (`Drive`, `DesktopBackground`, `Printers`, etc.) only contain Microsoft system handlers and can be deferred to a future enhancement if comprehensive enumeration is desired.

### Multi-Registration Awareness

Several handlers register under multiple HKCR locations (e.g., 7-Zip registers under `*`, `Directory`, `Folder`, and `opensearchfilefolderresult`). The implementation must:
1. Enumerate all paths and deduplicate by CLSID.
2. When disabling, apply the dash prefix in ALL registration locations for that CLSID.
3. When displaying, show a single entry per CLSID with a list of affected scopes.
