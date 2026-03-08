# Epic 2: Registry Path Research

**ThisIsMyPC — Shell & Explorer Customization**
**Researched March 2026 — Target: Windows 11 25H2**

---

> This document maps every registry path needed for stories 2.1–2.5, organized by story. Each entry includes the key path, value name, type, valid data, default, scope (HKCU vs HKLM), whether it requires an Explorer restart or reboot, and any known gotchas.

---

## Story 2.1: Registry Interop & Shell Module Scaffold

Story 2.1 is infrastructure — it builds `IRegistryService` and `RegistryService`. The paths below are consumed by stories 2.2–2.5, but 2.1's integration tests should exercise reads/writes against a sandbox key.

### Test Sandbox Key

```
HKCU\Software\ThisIsMyPC\Tests\
```

All integration tests create, read, write, and delete under this key. Tests must clean up after themselves and be tagged `[Trait("Category", "Integration")]`.

### Value Types to Support

The stories require at minimum: `REG_DWORD`, `REG_SZ`, `REG_EXPAND_SZ` (for environment variables), and `REG_MULTI_SZ` (for PATH-like values). `OperationResult<T>` must handle `AccessDenied` (HKLM writes without elevation) and `NotFound` (missing key/value).

---

## Story 2.2: Context Menu Handler Management

### Context Menu Handler Enumeration Paths

Context menu handlers are registered across multiple HKCR locations. To enumerate all handlers, scan all of these:

| Registry Path | Applies To |
|---|---|
| `HKCR\*\shellex\ContextMenuHandlers\` | All files |
| `HKCR\AllFilesystemObjects\shellex\ContextMenuHandlers\` | All files and file folders |
| `HKCR\Directory\shellex\ContextMenuHandlers\` | File folders (directories) |
| `HKCR\Directory\Background\shellex\ContextMenuHandlers\` | Desktop / folder background |
| `HKCR\Folder\shellex\ContextMenuHandlers\` | All folders (including virtual) |
| `HKCR\<ProgID>\shellex\ContextMenuHandlers\` | Per-file-type (e.g., `txtfile`, `Excel.Sheet.12`) |

Each handler subkey contains a `(Default)` REG_SZ whose value is the CLSID GUID of the handler DLL. To resolve the handler's DLL path and publisher, look up:

```
HKCR\CLSID\{<GUID>}\InprocServer32\(Default)   → DLL path
```

Then read the DLL's version info resource for publisher/company name (via `FileVersionInfo`).

### Disabling a Handler

There are two approaches, both widely used:

**Approach A — Prefix the CLSID with a dash:** Rename the `(Default)` value from `{GUID}` to `-{GUID}`. This is what ShellExView uses. Explorer ignores entries starting with `-`. Reversible by removing the prefix.

**Approach B — Rename the handler subkey:** Prefix the key name itself. Less common, more destructive.

**Recommendation for ThisIsMyPC:** Use Approach A (dash prefix). It's non-destructive, easily reversible, and is the industry-standard approach used by NirSoft's ShellExView. The `ChangeDescriptor` should record the original GUID as the previous value and the dashed GUID as the new value.

### Static Shell Entries (also relevant to 2.2)

Static context menu entries (non-COM, just commands) live under:

```
HKCR\*\shell\<VerbName>\command               → (Default) = command line
HKCR\Directory\shell\<VerbName>\command
HKCR\Directory\Background\shell\<VerbName>\command
HKCR\<ProgID>\shell\<VerbName>\command
```

These can be disabled by adding a `LegacyDisable` REG_SZ (empty) to the verb key, or hidden behind Shift+right-click by adding an `Extended` REG_SZ (empty).

### Explorer Restart Required

Yes — context menu changes require an Explorer restart to take effect, because shell extensions are loaded into the Explorer process.

---

## Story 2.3: Taskbar, Widgets & Classic Context Menu

All three settings live under the same parent key. All are HKCU, all are DWORDs, and all take effect after an Explorer restart (not a full reboot).

### Base Key

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced
```

### Classic Context Menu (Win10-style)

| Detail | Value |
|---|---|
| **Mechanism** | Create/delete a key that overrides the Win11 context menu COM object |
| **Key** | `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` |
| **Value** | `(Default)` = `""` (empty string, REG_SZ) |
| **Enable classic** | Key exists with empty default value |
| **Restore Win11 menu** | Delete the `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` key entirely |
| **Type** | REG_SZ |
| **Restart** | Explorer restart required |
| **Scope** | Per-user (HKCU) |

**Gotcha:** The `(Default)` value must be an empty string (`""`), NOT "value not set." Double-clicking and clicking OK without typing in regedit sets it to empty. Programmatically, write an empty REG_SZ.

**Gotcha:** The CLSID `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` is the `Windows.UI.FileExplorer.CLSID` that implements the modern context menu. Setting an empty InprocServer32 masks it.

### Taskbar Alignment

| Detail | Value |
|---|---|
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value name** | `TaskbarAl` (lowercase L, not uppercase I) |
| **Type** | REG_DWORD |
| **Data** | `0` = Left, `1` = Center (default) |
| **Restart** | Takes effect immediately or after Explorer restart |
| **Scope** | Per-user (HKCU) |

**Gotcha:** The value name ends in lowercase `l` (ell). Easy to misread as capital `I`.

### Widgets Toggle

| Detail | Value |
|---|---|
| **Key** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` |
| **Value name** | `TaskbarDa` |
| **Type** | REG_DWORD |
| **Data** | `0` = Hidden, `1` = Shown (default) |
| **Restart** | Usually immediate; may require sign-out on some builds |
| **Scope** | Per-user (HKCU) |

**UCPD status (ProcMon-validated 2026-03-07):** UCPD is NOT blocking HKCU writes to `TaskbarDa` on 25H2 currently. All writes return SUCCESS. Explorer immediately reads the new value after write (watching via `RegNotifyChangeKeyValue`). However, `RegistryService` should still catch `ACCESS_DENIED` and surface it as `ErrorCategory.ProtectedByPolicy` for forward-compatibility in case Microsoft re-enables protection in a future update.

**Full disable (policy-level, requires admin):**

```
HKLM\SOFTWARE\Policies\Microsoft\Dsh
AllowNewsAndInterests = DWORD 0
```

This completely disables the widgets feature, including Win+W. Use the HKCU toggle for the taskbar button only; use the HKLM policy for full suppression. The UI should make this distinction clear.

---

## Story 2.4: Explorer Preferences & Notification Suppression

### Explorer Preferences

All under:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced
```

| Setting | Value Name | Type | Values | Default | Restart |
|---|---|---|---|---|---|
| Show hidden files | `Hidden` | DWORD | `1` = Show, `2` = Don't show | `2` | Explorer refresh (F5) |
| Show file extensions | `HideFileExt` | DWORD | `0` = Show, `1` = Hide | `1` | Explorer refresh |
| Show protected OS files | `ShowSuperHidden` | DWORD | `1` = Show, `0` = Hide | `0` | Explorer refresh |
| Separate process for folders | `SeparateProcess` | DWORD | `1` = Yes, `0` = No | `0` | Explorer restart |
| Show sync provider notifications | `ShowSyncProviderNotifications` | DWORD | `0` = Off, `1` = On | `1` | Explorer refresh |

**Navigation pane** and **confirm delete dialog** are under a different key:

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer
```

| Setting | Value Name | Type | Values | Default |
|---|---|---|---|---|
| Launch Explorer to | `LaunchTo` | DWORD | `1` = This PC, `2` = Quick Access, `3` = Home (Win11) | `2` or `3` |

**Confirm file delete dialog:**

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer
```

| Setting | Value Name | Type | Values | Default |
|---|---|---|---|---|
| Confirm file delete | `ConfirmFileDelete` | DWORD | `1` = Show prompt, `0` = Skip | Depends on Recycle Bin properties |

**Note:** The confirm delete prompt is actually controlled by the Recycle Bin properties (right-click Recycle Bin → Properties → "Display delete confirmation dialog"). The registry path above is the policy override. Consider also checking the shell property directly.

### Notification Suppression (ContentDeliveryManager)

All under:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager
```

| What It Suppresses | Value Name | Type | 0 = | 1 = (Default) |
|---|---|---|---|---|
| Tips and suggestions when using Windows | `SubscribedContent-338389Enabled` | DWORD | Disabled | Enabled |
| "Get started" suggestions / finish setup prompts | `SubscribedContent-310093Enabled` | DWORD | Disabled | Enabled |
| Suggested content in Settings app | `SubscribedContent-338393Enabled` | DWORD | Disabled | Enabled |
| App suggestions (auto-install) | `SilentInstalledAppsEnabled` | DWORD | Disabled | Enabled |
| Spotlight / lock screen ads | `RotatingLockScreenOverlayEnabled` | DWORD | Disabled | Enabled |
| Lock screen "fun facts, tips, tricks" | `SubscribedContent-338387Enabled` | DWORD | Disabled | Enabled |
| Rotating lock screen (Spotlight images) | `RotatingLockScreenEnabled` | DWORD | Disabled | Enabled |
| Suggested content in Settings (additional) | `SubscribedContent-353694Enabled` | DWORD | Disabled | Enabled |
| Suggested content in Settings (additional 2) | `SubscribedContent-353696Enabled` | DWORD | Disabled | Enabled |
| OEM preinstalled apps | `OemPreInstalledAppsEnabled` | DWORD | Disabled | Enabled |
| Preinstalled apps | `PreInstalledAppsEnabled` | DWORD | Disabled | Enabled |
| Software landing (tips balloon) | `SoftLandingEnabled` | DWORD | Disabled | Enabled |

### Additional Suppression Keys (outside ContentDeliveryManager)

| What It Suppresses | Key | Value Name | Type | Data |
|---|---|---|---|---|
| Welcome experience / "finish setup" | `HKCU\...\UserProfileEngagement` | `ScoobeSystemSettingEnabled` | DWORD | `0` |
| Spotlight collection on desktop | `HKCU\Software\Policies\Microsoft\Windows\CloudContent` | `DisableSpotlightCollectionOnDesktop` | DWORD | `1` |
| Windows tips (GPO / all users) | `HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent` | `DisableSoftLanding` | DWORD | `1` |
| Search highlights | `HKCU\...\SearchSettings` | `IsDynamicSearchBoxEnabled` | DWORD | `0` |

The full path for UserProfileEngagement is:
```
HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement
```

**Gotcha:** Windows feature updates (major version upgrades) have been observed to reset some ContentDeliveryManager values back to `1`. The scan should detect drift and flag values that have changed since the last apply.

---

## Story 2.5: Environment Variable & PATH Editor

### Environment Variable Registry Locations

| Scope | Key | Type |
|---|---|---|
| **User** | `HKCU\Environment` | REG_SZ or REG_EXPAND_SZ |
| **System** | `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` | REG_SZ or REG_EXPAND_SZ |

The `PATH` variable specifically uses `REG_EXPAND_SZ` because it can contain `%SystemRoot%` and similar expandable references.

### Reading

Enumerate all value names and data under each key. The value name is the variable name, the data is the variable value.

### Writing

After modifying a value, broadcast `WM_SETTINGCHANGE` with `lParam = "Environment"` so that running processes pick up the change without requiring a reboot:

```csharp
SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "Environment",
                   SMTO_ABORTIFHUNG, 5000, out _);
```

This is critical — without the broadcast, only new processes will see the change. The Settings app and `setx.exe` both do this automatically, but direct registry writes do not.

### PATH Specifics

PATH is a single REG_EXPAND_SZ value containing semicolon-delimited entries. The PATH editor in story 2.5 should split on `;`, present each entry as a row, and rejoin on save. The `ChangeDescriptor` should record the full before/after string, and the review panel should compute a diff showing added/removed/reordered entries.

**Gotcha:** PATH has a practical length limit of approximately 2048 characters (some tools fail beyond this, though the registry itself can store longer). Warn the user if PATH is approaching this limit.

**Gotcha:** System PATH and User PATH are merged at logon. The effective PATH is System PATH + User PATH. The UI should display both but make clear which scope each entry belongs to.

### System Environment Variables (Requires Admin)

Writing to `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` requires admin elevation. The UI should clearly indicate which variables require admin and prompt for elevation when modifying system-scope variables.

---

## Cross-Cutting Concerns

### Explorer Restart vs. Reboot vs. Immediate

| Change Type | Takes Effect |
|---|---|
| Explorer\Advanced DWORDs (most) | Explorer refresh (F5) or restart |
| Context menu handlers | Explorer restart |
| Classic context menu toggle | Explorer restart |
| TaskbarAl | Usually immediate |
| TaskbarDa (Widgets) | Usually immediate, sometimes sign-out |
| ContentDeliveryManager values | Next time the relevant UI element renders |
| Environment variables | After WM_SETTINGCHANGE broadcast |
| System environment variables | After WM_SETTINGCHANGE broadcast + new process |

### HKCR Is a Merged View

`HKEY_CLASSES_ROOT` is a merged view of `HKLM\SOFTWARE\Classes` (machine-wide) and `HKCU\Software\Classes` (per-user). Per-user entries override machine-wide. When reading, use HKCR. When writing, decide explicitly whether to write to the machine or user hive. For context menu handler disable/enable, writing to the machine hive (HKLM) requires admin elevation.

### UCPD Protection (Windows 11 24H2+)

The User Configuration Protection Driver (`UCPD.sys`) is a Windows 11 component (introduced 24H2) that can write-protect certain HKCU values (notably `TaskbarDa`). **ProcMon validation (2026-03-07) confirmed UCPD is NOT blocking HKCU writes on 25H2.** All tested writes returned SUCCESS. However, this could change in future updates — `RegistryService` should catch `ACCESS_DENIED` on HKCU writes and surface it as `ErrorCategory.ProtectedByPolicy` for forward-compatibility.

---

## ProcMon Validation Results — Windows 11 25H2 (March 2026)

Validated 2026-03-07 by Sam via ProcMon trace.

### TaskbarDa (Widgets Toggle)

- **Writer:** SystemSettings.exe
- **Result:** All SUCCESS — no UCPD write-protection on 25H2
- **Explorer behavior:** Immediately reads the new value after write (watching via `RegNotifyChangeKeyValue`)
- **Restart required:** None — takes effect immediately

### SubscribedContent-338389Enabled (Tips & Suggestions)

- **Writer:** SystemSettings.exe
- **Result:** All SUCCESS
- **Explorer behavior:** After each write, Explorer reads `SoftLandingEnabled` from the same key — both values are consumed live
- **No new SubscribedContent IDs observed** in 25H2 beyond what's documented above

### Implementation Notes from Validation

- Explorer actively watches `Explorer\Advanced` and `ContentDeliveryManager` keys — many settings are live-reloadable without Explorer restart
- Context menu changes (story 2.2) and classic context menu toggle (story 2.3) still require Explorer restart — these are shell extension loads, not watched values
- For environment variable writes (story 2.5), broadcast `WM_SETTINGCHANGE` with lParam `"Environment"` after every write
- UCPD is not currently blocking HKCU writes, but `RegistryService` should still catch `ACCESS_DENIED` and surface it as `ErrorCategory.ProtectedByPolicy` for forward-compatibility

---

## Recommended Validation Steps

For any new registry paths added in future stories:

1. **ProcMon trace:** Filter to RegSetValue/RegQueryValue on each path while toggling the corresponding Settings toggle. Confirm the paths match.
2. **ShellExView:** Cross-reference the context menu handler enumeration against NirSoft's ShellExView output to ensure completeness.
3. **winutil source:** Chris Titus Tech's winutil uses many of the same ContentDeliveryManager keys — cross-reference for any new SubscribedContent IDs.
4. **Reboot/restart testing:** For each setting, confirm the actual restart requirement (refresh vs. Explorer restart vs. sign-out vs. reboot).
