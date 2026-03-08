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

## Analysis

### Global Entries (Present on All File Types: PNG, HTML, PDF)
These entries appear on every file right-click and are registered on `HKCR\*` or equivalent global keys:
- Open
- WinRAR →
- Move to OneDrive
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

| Entry | PNG | HTML | PDF | Folder | Folder BG |
|---|---|---|---|---|---|
| Edit with Photos | ✓ | | | | |
| Convert to Adobe PDF | ✓ | ✓ | | | |
| Create with Designer | ✓ | | | | |
| Ask Copilot | ✓ | | ✓ | | |
| Edit with Paint | ✓ | | | | |
| Set as desktop background | ✓ | | | | |
| Print | ✓ | | | | |
| Resize with Image Resizer | ✓ (x2) | | | | |
| Rotate right/left | ✓ | | | | |
| Cast to Device → | ✓ | | | | |
| Edit with Adobe Acrobat | | | ✓ | | |
| Combine files in Acrobat... | | | | ✓ | |
| Open in new tab/window | | | | ✓ | |
| Pin to Quick access | | | | ✓ | |
| WizTree | | | | ✓ | ✓ |
| Open in Terminal | | | | ✓ | ✓ |
| Open with Visual Studio | | | | ✓ | ✓ |
| Open with VS Code | | | | ✓ | |
| Include in library → | | | | ✓ | |
| Pin to Start | | | | ✓ | |

### Vendor Footprint Summary

| Vendor | Entries | Contexts |
|---|---|---|
| **Adobe** | Convert to Adobe PDF, Edit with Adobe Acrobat, Combine files in Acrobat | Files (global), PDF (specific), Folders |
| **Microsoft (non-OS)** | Ask Copilot, Create with Designer, Move to OneDrive | Selective per file type |
| **PowerToys** | Unlock with File Locksmith (x2!), Rename with PowerRename, Resize with Image Resizer (x2 on PNG) | Global + image-specific |
| **WinRAR** | WinRAR → | Global (files + folders) |
| **7-Zip** | 7-Zip → | Global (files + folders) |
| **WizTree** | WizTree | Folders + folder background |
| **Notepad++** | Edit with Notepad++ | Global (files) |
| **VS Code** | Open with Code | Global (files) + folders |
| **Visual Studio** | Open with Visual Studio | Folders + folder background |

### Duplicate Entry Bug
- **File Locksmith** appears twice in every file context menu (Sections 1 and 5) and twice in the folder context menu (Sections 1 and 5). This is a PowerToys registration issue.
- **Image Resizer** appears twice on PNG files (Sections 1 and 2). Another PowerToys double-registration.

### Key Takeaways
1. The bottom half of every file context menu (Give access to → Properties) is essentially identical regardless of file type — this is the "persistent core."
2. Adobe registers different handlers per file type rather than one global entry, making it harder to clean up with a single toggle.
3. PowerToys is the worst offender for duplicate entries, registering File Locksmith in two different handler locations.
4. Folder background context menus are significantly cleaner than file or folder menus — fewer apps register background handlers.
5. The global `*` registrations (WinRAR, 7-Zip, Notepad++, etc.) are the primary bloat contributors since they appear on every single file type.
