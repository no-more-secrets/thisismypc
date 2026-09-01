# ExplorerPatcher Dependency Study

AI-written study from March 2026, kept as rationale for the Explorer and
Context Menus modules. Epic and Story numbers below refer to the retired
first-chapter plan; the outcome (port the registry recipes, never the hooking)
is recorded in planning/refinement-backlog.md under the ExplorerPatcher gap
analysis.

**Repository:** https://github.com/valinet/ExplorerPatcher
**License:** GPLv2
**Language:** C (77%) / C++ (23%)
**Maintainer:** valinet
**Purpose:** Hooks deep into Windows Explorer internals to restore Windows 10 behaviors on Windows 11, classic taskbar, context menus, Start menu, Alt+Tab, and more.

> This document is a living analysis for the ThisIsMyPC project. It documents EP's architecture, capabilities, and integration potential with our C#/.NET NativeAOT codebase.

---

## Table of Contents

1. [Structural Analysis](#1-structural-analysis)
2. [Hooking Infrastructure](#2-hooking-infrastructure)
3. [Capability Inventory](#3-capability-inventory)
4. [Integration Assessment](#4-integration-assessment)

---

## 1. Structural Analysis

### 1.1 Directory Tree

```
ExplorerPatcher/
├── ExplorerPatcher/              # Core DLL, main hooking engine (the heart of EP)
│   ├── dllmain.c                 # 13,300+ lines, initialization, all hooks, COM interception
│   ├── hooking.h                 # SlimDetours wrapper (inline function hooking)
│   ├── symbols.h/c               # PDB symbol loading for offset-based hooking
│   ├── utility.c/h               # Helper functions
│   ├── lvt.c/h                   # Large virtual table / COM utilities
│   ├── StartMenu.c/h             # Start menu patching
│   ├── ArchiveMenu.c             # Archive context menu support
│   ├── TwinUIPatches.cpp         # Symbol resolution for twinui.pcshell.dll
│   └── inc/                      # Headers
├── ep_gui/                       # Settings GUI, Win32 native dialog
│   ├── GUI.c                     # 4,090+ lines, settings UI
│   ├── dllmain.cpp               # Module init
│   └── resources/                # settings.reg templates, icons
├── ep_setup/                     # Installer, extracts, registers, restarts Explorer
│   ├── ep_setup.c                # 1,400+ lines, installation logic
│   ├── rijndael-alg-fst.c/h      # AES-256 for encrypted ZIP payload
│   └── resources/                # Icons, manifests
├── ep_setup_patch/               # Hash verification utility (post-build step)
├── ep_startmenu/                 # Start menu modifications (XAML metadata patching)
│   ├── ep_sm_main.c              # Classic start menu restoration
│   └── ep_sm_main_cpp.cpp        # C++ wrapper
├── ep_dwm/                       # (Empty, git submodule placeholder for DWM service)
├── ep_weather_host/              # Weather widget, COM service with WebView2
├── ep_weather_host_stub/         # IDL type library for weather COM interface
├── ep_extra/                     # Extension loader, discovers ep_extra_*.dll modules
├── ep_extra_valinet.win7alttab/  # Windows 7 Alt+Tab replacement
├── ep_generate_release_name/     # Build utility, version string extraction
├── ep_generate_release_description/ # Build utility, changelog extraction
├── ExplorerPatcher-L10N/         # Localization (git submodule, 44+ languages)
├── libs/
│   ├── libvalinet/               # Utility library (PDB parsing, IAT patching, WinRT, toast)
│   ├── sws/                      # Simple Window Switcher (Alt+Tab UI)
│   └── zlib/                     # Compression (ZIP packaging for installer)
├── ExplorerPatcher.sln           # VS 2022 solution (11 projects)
├── version.h                     # Version defines (aligned to Windows build numbers)
├── debug.h                       # Debug console allocation
└── BuildDependencies*.bat        # CMake build scripts for zlib
```

### 1.2 Build System

**Toolchain:** Visual Studio 2022, MSVC v143, Windows SDK 10.0, C++20 standard.

**Solution:** 11 C/C++ projects. Multi-architecture: Win32, x64, ARM64, ARM64EC.

**NuGet Dependencies:**
| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Windows.ImplementationLibrary | 1.0.250325.1 | WIL C++ utilities |
| KNSoft.SlimDetours | 1.1.4-beta | Inline hooking (open-source Detours replacement) |
| Microsoft.Web.WebView2 | 1.0.3405.78 | WebView2 for weather widget UI |

**Git Submodules:**
| Submodule | Purpose |
|-----------|---------|
| libs/libvalinet | IAT patching (`iatpatch.h`), PDB parsing (`pdb.h`), toast notifications, OS version detection |
| libs/sws | Simple Window Switcher (Alt+Tab UI implementation) |
| libs/zlib | Compression for installer ZIP packaging |
| ep_dwm | Desktop Window Manager service (empty in current build) |
| ExplorerPatcher-L10N | Localization resources |

### 1.3 Output Artifacts

| Binary | Type | Purpose |
|--------|------|---------|
| `ExplorerPatcher.amd64.dll` | DLL | Main patcher for x64 systems |
| `ExplorerPatcher.arm64.dll` | DLL | Main patcher for ARM64 systems |
| `ExplorerPatcher.IA-32.dll` | DLL | Legacy 32-bit support |
| `ExplorerPatcher.arm64ec.dll` | DLL | ARM64 with x64 emulation compat |
| `dxgi.dll` (copy of above) | DLL | DXGI masquerade, injection entry point |
| `ep_gui.dll` | DLL | Settings UI (Win32 dialog) |
| `ep_startmenu.dll` → `wincorlib.dll` | DLL | Start menu patches |
| `ep_dwm_svc.exe` | EXE | DWM service |
| `ep_weather_host.dll` | DLL | Weather widget COM service |
| `ep_weather_host_stub.dll` | DLL | Weather COM type library |
| `ep_extra.dll` | DLL | Extension loader |
| `ep_extra_valinet.win7alttab.dll` | DLL | Win7 Alt+Tab |
| `ep_taskbar.N.dll` (N=0-5) | DLL | Taskbar patches |
| `ep_setup.exe` | EXE | Installer (ships all DLLs as embedded encrypted ZIP) |

### 1.4 Injection & Persistence

EP uses a **triple injection strategy** to get code running inside explorer.exe:

**Method 0, DXGI DLL Masquerade:**
- `ExplorerPatcher.amd64.dll` is renamed to `dxgi.dll` and placed in `C:\Windows\` and `C:\Windows\SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\`
- Exports `DXGIDeclareAdapterRemovalSupport()` and `CreateDXGIFactory1()`, when explorer.exe imports DXGI, it loads EP's DLL first (DLL search order hijacking)
- Triggers `EntryPoint(DLL_INJECTION_METHOD_DXGI)`

**Method 1, COM Registration:**
- DLL registered as COM server via `regsvr32.exe`
- Registry: `HKLM\SOFTWARE\Classes\CLSID\{EP_CLSID}\InProcServer32` → `C:\Program Files\ExplorerPatcher\ExplorerPatcher.amd64.dll`
- Exports `DllGetClassObject()`, any COM instantiation triggers `EntryPoint(DLL_INJECTION_METHOD_COM)`

**Method 2, Internal Start Injection:**
- From within explorer.exe, EP injects into `StartMenuExperienceHost.exe` via `InjectStartFromExplorer()`

**Process Detection (dllmain.c:13108-13121):**
- Detects which process loaded the DLL: explorer.exe, StartMenuExperienceHost.exe, ShellExperienceHost.exe
- Skips SearchIndexer.exe
- Routes to appropriate `Inject*()` function based on process + method

### 1.5 Symbol-Based Patching (Update Survival)

This is EP's key resilience mechanism. Rather than hardcoding function offsets that break with every Windows update:

1. **At install time**, EP downloads `.pdb` files from Microsoft's symbol server for:
   - `explorer.exe` (6 symbols)
   - `twinui.pcshell.dll` (7 symbols, context menus, multitasking)
   - `StartDocked.dll` (5 symbols, start menu)
   - `StartUI.dll` (1 symbol)

2. **At runtime**, EP parses cached PDBs to resolve function offsets, then hooks at those addresses.

3. **Fallback**: Binary pattern scanning when PDBs aren't available (dllmain.c:9712-9925). Uses byte patterns with wildcard masks:
   ```c
   FindPattern(hModule, moduleSize, "\x48\x8B\x93\x00...", "xxx????xxx...");
   ```

**Windows Build Detection:**
- Fine-grained: `IsWindows11()`, `IsWindows11Version22H2OrHigher()`, `IsWindows11Version22H2Build2134OrHigher()`, etc.
- Version-specific code paths throughout, EP has been maintained across multiple Win11 builds.

---

## 2. Hooking Infrastructure

### 2.1 Hooking Technologies

EP uses a **four-layer hooking stack**:

| Layer | Technology | Library | Use Case |
|-------|-----------|---------|----------|
| **IAT Patching** | Import Address Table rewriting | libvalinet `VnPatchIAT()` | Most API interceptions (~130+ patches across 20+ DLLs) |
| **Inline Hooking** | Function prologue patching | SlimDetours `SlimDetoursInlineHook()` | Critical paths where IAT isn't available (direct calls) |
| **COM Proxy Wrapping** | Return proxy objects from `CoCreateInstance` | Custom code | COM interfaces where vtable patching is blocked by CFG |
| **VTable Patching** | Direct vtable entry replacement | `VirtualProtect` + pointer write | Limited use where CFG doesn't apply (XAML, ITaskGroup) |

### 2.2 IAT Patching Details

`VnPatchIAT(moduleName, functionName, hookFunction)` rewrites the Import Address Table of a loaded DLL so all calls to `functionName` within that module are redirected to `hookFunction`.

**Modules Patched and Functions Hooked:**

**shell32.dll** (dllmain.c:9489-9530):
- `TrackPopupMenu`, context menu appearance control
- `CoCreateInstance`, COM object creation interception
- `SystemParametersInfoW`, fake SPI_GETSCREENREADER for immersive menu bypass
- `CreateWindowExW`, `SetWindowLongPtrW`, window creation/properties

**ExplorerFrame.dll** (dllmain.c:9546-9603):
- `TrackPopupMenu`, `SystemParametersInfoW`, `CoCreateInstance`
- `SHCreateWorkerWindow` (ordinal 188 in shcore.dll)
- `CompareStringOrdinal`, `LoadAcceleratorsW`
- `GetSystemMetricsForDpi`

**Windows.UI.FileExplorer.dll** (dllmain.c:9605-9632):
- `TrackPopupMenu`, `SystemParametersInfoW`
- `CreateWindowExW`, `SetWindowLongPtrW`

**explorer.exe** (dllmain.c:10884-10960):
- `TrackPopupMenuEx`, menu display
- `CoCreateInstance`, COM interception
- `RegOpenKeyExW`, `RegQueryValueExW`, `RegSetValueExW`, `RegCreateKeyExW`, registry rewriting
- `LoadMenuW`, `DeleteMenu`, `SetRect`, `SendMessageW`
- `OpenThemeDataForDpi`, `DrawThemeBackground`, `CloseThemeData`, `DrawThemeTextEx`, theme/classic styling
- `SetWindowCompositionAttribute`, visual composition
- `ShellExecuteW`/`ShellExecuteExW`, shell execution
- `SetChildWindowNoActivateHook` (ordinal 2005)
- `DwmUpdateThumbnailProperties`

**user32.dll** (dllmain.c:10857-10865):
- `CreateWindowInBand`, `GetWindowBand`, `SetWindowBand`
- `SetWindowCompositionAttribute`
- `NtUserFindWindowEx` (conditional on build)

**System tray modules**: pnidui.dll, stobject.dll, twinui.dll, sndvolsso.dll, bthprops.dll, InputSwitch.dll, all get `TrackPopupMenu[Ex]` and/or `CoCreateInstance` hooks.

### 2.3 Inline Hooking (SlimDetours)

Used sparingly for functions that aren't in IAT (called directly rather than via import):

- `ntdll!RtlQueryFeatureConfiguration`, disables feature flags that break customization (dllmain.c:9652-9655)
- `ImmersiveContextMenuHelper::ApplyOwnerDrawToMenu` in shell32.dll, found via pattern scan, hooked inline (dllmain.c:2479-2519)
- `ImmersiveContextMenuHelper::RemoveOwnerDrawFromMenu` in twinui.pcshell.dll, symbol-resolved

### 2.4 COM Proxy Wrapping

**Critical finding: EP cannot vtable-patch COM objects in explorer.exe because of Control Flow Guard (CFG).** The code explicitly documents this (dllmain.c:8395-8434):

```c
// Cannot patch the vtable of the COM object because the executable is protected
// by control flow guard and we would make a jump to an invalid site
```

**Workaround:** EP returns **proxy COM objects** from its `CoCreateInstance` hook that wrap the original object and override specific methods:

| CLSID | Proxy | Purpose |
|-------|-------|---------|
| `CLSID_InputSwitchControl` | `CInputSwitchControlProxySV2` | IME style override |
| `CLSID_TrayUIComponent` | `EPTrayUIComponent` | Win10 taskbar on Win11 |

**COM Creation Blocking** (returns `REGDB_E_CLASSNOTREG`):

| Module | CLSID Blocked | Purpose |
|--------|--------------|---------|
| ExplorerFrame | `CLSID_XamlIslandViewAdapter` | Block modern XAML view |
| ExplorerFrame | `CLSID_UIRibbonFramework` | Block modern ribbon |
| Shell32 | `CLSID_FileExplorerFolderView` | Block modern folder view |

### 2.5 Registry Rewriting

EP hooks registry functions in explorer.exe to transparently redirect settings:

| Hook | Rewrite |
|------|---------|
| `RegCreateKeyExW` | `MMStuckRects3` → `MMStuckRectsLegacy` |
| `SHGetValueW` | `StuckRects3` → `StuckRectsLegacy` |
| `OpenRegStream` (ordinal 85) | `TaskbarWinXP` → `TaskbarWinEP` |
| `RegOpenKeyExW` | `TrayNotify` → `TrayNotSIB` |

This lets EP maintain separate taskbar/tray settings without conflicting with Windows defaults.

### 2.6 Initialization Flow

```
DllMain()
  → stores hModule only (minimal init)

DXGIDeclareAdapterRemovalSupport() / DllGetClassObject()
  → EntryPoint(dwMethod)
    → process detection (explorer.exe / StartMenuExperienceHost / ShellExperienceHost)
    → skip SearchIndexer.exe
    → Inject(bIsExplorer)
      → create funchook object
      → MonitorSettings(), registry change notification thread (12+ watched paths)
      → InjectBasicFunctions(bIsExplorer, TRUE)
        → IAT patching: shell32, ExplorerFrame, Windows.UI.FileExplorer
        → SlimDetours: RtlQueryFeatureConfiguration
      → [explorer.exe only]:
        → install SlimDetours funchook
        → LoadSymbols(), download/parse PDBs
        → IAT patching: user32, registry APIs, COM, theme, DWM
        → pattern-scan for explorer.exe internal offsets
        → COM proxy registration
        → taskbar, system tray, Start menu hooks
```

---

## 3. Capability Inventory

### 3.1 Context Menu Manipulation

**Relevance to ThisIsMyPC: HIGH, directly relevant to Epic 2 (Shell/Explorer probe) and Stories 2.7-2.10.**

**What it does:** Restores the Windows 10 classic context menu on Windows 11, removing the modern "show more options" truncated menu.

**How it works, NOT by blocking the CLSID:**

EP does **not** block `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}`. Instead, it intercepts at the **presentation layer**:

1. **Hook `TrackPopupMenu`/`TrackPopupMenuEx`** across shell32, ExplorerFrame, explorer.exe, Windows.UI.FileExplorer, and all system tray modules (dllmain.c:2532-2690)

2. **Detect immersive menus** via `EnumPropsA()`, checks if menu items have owner-drawn formatting applied by the immersive context menu system (dllmain.c:2558-2560)

3. **Strip `MFT_OWNERDRAW` flag** from all menu items recursively using `SetMenuItemInfoW()` (dllmain.c:2402-2420). This forces Win32 classic rendering.

4. **Fallback**: If running in explorer.exe with symbol access, calls `ImmersiveContextMenuHelper::RemoveOwnerDrawFromMenu()` directly (dllmain.c:2571, 2659), resolved from twinui.pcshell.dll PDB symbols.

5. **Taskbar vs Explorer dual logic** (dllmain.c:2547, 2634):
   ```c
   BOOL bDisable = IsTaskbar ? !bSkinMenus : bDisableImmersiveContextMenu;
   ```

**Key source files:**
- `dllmain.c` lines 2400-2700, menu hook implementations
- `dllmain.c` lines 2479-2519, `HookImmersiveMenuFunctions()` pattern scan + inline hook
- `TwinUIPatches.cpp` lines 3469-3494, symbol resolution for twinui.pcshell functions
- `symbols.h` lines 27-29, symbol name definitions

**Per-module hook macro** (`DEFINE_IMMERSIVE_MENU_HOOK`): Generates specialized hooks for Shell32, ExplorerFrame, Explorer, Pnidui, Sndvolsso, InputSwitch (dllmain.c:2472-2477).

**Registry control:**
- `HKCU\Software\ExplorerPatcher\DisableImmersiveContextMenu` (DWORD), master switch
- `HKCU\Software\ExplorerPatcher\SkinMenus` (DWORD), owner-draw application toggle

**EP also writes these CLSIDs to disable modern handlers at the registry level:**
- `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`, modern context menu
- `HKCU\Software\Classes\CLSID\{056440FD-8568-48e7-A632-72157243B55B}\InprocServer32`, additional modern handler

**EP does NOT intercept IContextMenu/IContextMenu2/IContextMenu3 interfaces directly.** It operates purely at the presentation/rendering layer.

### 3.2 Taskbar Modifications

**Relevance to ThisIsMyPC: MEDIUM, hooking patterns are reusable even if we don't ship taskbar features.**

**What it does:** Restores Windows 10-style taskbar on Windows 11, uncombined buttons, labels, drag-and-drop, multi-monitor taskbar positioning.

**How it works:**
- Intercepts `CLSID_TrayUIComponent` via `CoCreateInstance` hook → returns `EPTrayUIComponent` proxy (dllmain.c:8438-8443)
- Hooks `CTaskBand_CreateInstance` via symbol/pattern resolution
- Patches `ITrayUIHost` vtable via pattern-matched offsets
- Registry rewriting: `MMStuckRects3` → `MMStuckRectsLegacy`, `TrayNotify` → `TrayNotSIB` for separate settings
- Hooks 40+ functions in explorer.exe for taskbar behavior

**Key technical patterns:**
- COM proxy wrapping to bypass CFG
- Registry redirection for non-destructive settings isolation
- Symbol-based function discovery for resilience

### 3.3 Shell COM Interception

**Relevance to ThisIsMyPC: HIGH, directly relevant to our context menu probe and shell integration work.**

**Hooked shell COM interfaces:**
- `CoCreateInstance` is hooked in shell32, ExplorerFrame, explorer.exe, pnidui, stobject, EP can intercept creation of ANY COM object in these modules
- Specific CLSIDs intercepted: InputSwitchControl, TrayUIComponent, XamlIslandViewAdapter, UIRibbonFramework, FileExplorerFolderView
- COM proxy pattern wraps original objects with custom implementations

**File Explorer command bar control** (dllmain.c:8178-8190):
- `FileExplorerCommandUI` registry value controls ribbon/command bar style
- Values 0-4 map to different UI modes (modern, legacy, blocked)

**No direct IShellBrowser/IShellView/IShellFolder hooking**, EP operates at the COM creation and menu presentation layers, not at the shell namespace level.

### 3.4 Explorer Process Injection/Loading

**Relevance to ThisIsMyPC: LOW for direct use (we don't inject into explorer.exe), HIGH for understanding Windows shell loading.**

See [Section 1.4, Injection & Persistence](#14-injection--persistence).

The DXGI masquerade technique is clever but requires placing files in `C:\Windows\` (admin + TrustedInstaller territory). The COM registration approach is more conventional. Neither is applicable to our architecture (we read shell state, we don't inject).

### 3.5 Registry Manipulation Patterns

**Relevance to ThisIsMyPC: HIGH, directly relevant to our registry probe architecture.**

**EP's settings hive:** `HKCU\Software\ExplorerPatcher`, all settings stored here.

**Registry read/write patterns:**
- `RegQueryValueExW` / `RegGetValueW` for reading with fallback to legacy path (`HKCU\...\Explorer\ExplorerPatcher`)
- `RegNotifyChangeKeyValue` for real-time setting change monitoring (12+ watched paths)
- Callback-based architecture for async setting updates

**Enforcement mechanism handling:** EP does NOT handle UCPD, GPCache, Tamper Protection, or TrustedInstaller. It writes to `HKCU` (user hive) which avoids most enforcement mechanisms. When it writes to `HKLM` (installer), it requires admin elevation.

**Relevant registry paths EP uses:**
| Path | Purpose |
|------|---------|
| `HKCU\Software\ExplorerPatcher` | All EP settings |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced` | Start_ShowClassicMode |
| `HKCU\Software\Classes\CLSID\{86ca1aa0-...}\InprocServer32` | Modern context menu disable |
| `HKCU\Software\Classes\CLSID\{2cc5ca98-...}\ShellFolder` | Search box visibility |
| `HKCU\SOFTWARE\Microsoft\Accessibility` | TextScaleFactor |

### 3.6 Settings/Configuration

**Relevance to ThisIsMyPC: LOW, EP uses a different architecture than ours.**

**Storage:** Pure registry (`HKCU\Software\ExplorerPatcher`). No config files.

**GUI:** Win32 native dialog (`ep_gui.dll`). Reads `settings.reg` templates with special formatting annotations (`;b`, `;c`, `;x`, `;t`, `;q` prefixes for UI hints).

**Communication:** No IPC between GUI and core DLL. Shared registry backend. Core DLL monitors registry for changes via `RegNotifyChangeKeyValue`.

**Update settings:** `UpdatePolicy` (0=Auto, 1=Notify, 2=Manual), `UpdateURL`, `UpdateURLStaging`.

### 3.7 Update Mechanism

**Relevance to ThisIsMyPC: LOW, different architecture.**

- GitHub releases API polling with configurable endpoints
- WinInet HTTP + SHA-256 hash verification
- Silent update mode: downloads, runs `ep_setup.exe /update_silent`, restarts Explorer
- User Agent: `"ExplorerPatcher"`
- 600-second timeout

### 3.8 Start Menu

**Relevance to ThisIsMyPC: LOW.**

- Patches XAML metadata GUID in-memory to swap StartDocked for StartUI
- Binary-patches `StartMenuExperienceHost::App::SetExperienceManagerPropertiesAsync()` to `ret` to prevent crashes
- Supports loading custom `StartUI_.dll` / `JumpViewUI_.dll`

### 3.9 Alt+Tab Replacement

**Relevance to ThisIsMyPC: LOW.**

- Loads system `AltTab.dll` and patches its IAT
- Hooks DWM, theme, message, and window APIs within AltTab module
- Registers as COM ShellServiceObject for lifecycle management

### 3.10 Weather Widget

**Relevance to ThisIsMyPC: NONE.**

- COM service with WebView2 rendering
- Network connectivity monitoring
- Not relevant to ThisIsMyPC's scope

---

## 4. Integration Assessment

### 4.1 License Compatibility

**ExplorerPatcher:** GPLv2
**ThisIsMyPC (public repo):** GPLv2

| Integration Approach | Permitted? | Notes |
|---------------------|-----------|-------|
| **Study and reimplement** | Yes | Clean-room or referenced reimplementation is fine. We're calling the same Windows APIs. |
| **Direct code reuse (copy C into our repo)** | Yes, with GPLv2 compliance | Must maintain GPLv2 license, include copyright notice, provide source. Our public repo is already GPLv2. |
| **Link to EP DLLs (P/Invoke)** | Yes | GPLv2 allows linking. Our GPLv2 code linking to GPLv2 code is straightforward. |
| **Fork EP components as standalone DLLs** | Yes | Must remain GPLv2. |
| **Use in private/proprietary repo** | No | GPLv2 code cannot be incorporated into proprietary code. Our security service (private repo) cannot use EP code. |

**Bottom line:** Full compatibility for the public repo. Study-and-reimplement is the cleanest path since we're C#/.NET and EP is C, we'd be calling the same Win32 APIs through CsWin32 rather than copying C code.

### 4.2 C/C++ Integration Patterns

ThisIsMyPC is C#/.NET 10 NativeAOT. EP is raw C/C++ Win32. Options:

| Approach | Feasibility | Recommended? |
|----------|-------------|-------------|
| **P/Invoke into EP DLLs** | Technically possible but EP DLLs are designed for injection, not library use | No, EP's DLLs hook into process internals, not suitable as general-purpose libraries |
| **Study EP → reimplement in C# with CsWin32** | Excellent, EP documents the exact APIs, CLSIDs, registry paths, and patterns | **Yes, primary approach** |
| **Build thin C/C++ interop layer** | Possible for undocumented APIs that can't be called from C# | Maybe, only if specific APIs resist CsWin32 |
| **Use EP as API reference** | Ideal, EP's code maps directly to Win32 API calls we can make from C# | **Yes, primary approach** |
| **Fork EP components** | Possible but introduces C/C++ build dependency | No, adds complexity without clear benefit |

**Recommended strategy:** Use EP as a **reference implementation**. Its C code maps 1:1 to the Win32 APIs we call through CsWin32/LibraryImport. The value is in understanding *which* APIs to call, *what* CLSIDs to intercept, *what* registry paths matter, and *what* patterns survive Windows updates.

### 4.3 Risk Assessment

**Stability/Compatibility:**
- EP has been maintained across multiple Windows 11 builds (22H2, 23H2, 24H2). The symbol-based patching approach provides resilience.
- EP's pattern scanning is fragile by nature, byte patterns break when Microsoft recompiles binaries.
- EP requires killing and restarting explorer.exe during install. We do not.

**Relevance of risks to ThisIsMyPC:**
- **We do NOT inject into explorer.exe**, most of EP's stability risks don't apply to us.
- **We read shell state, not modify it** (for the probe/inventory layer), much lower risk profile.
- **Our enforcement layer (Epic 26) writes registry**, EP's approach of writing to HKCU to avoid enforcement is relevant and informative.
- **CFG/ACG concerns** (from our `nativeaot-runtime-integrity-research.md`) are validated by EP's experience, EP explicitly documents that CFG prevents vtable patching and uses proxy COM objects as a workaround.

**Windows Update breakage:**
- EP uses symbol server downloads + pattern scanning as a two-tier resilience strategy.
- For our use case (reading registry, enumerating COM registrations), we're at much lower risk since we're not hooking internal functions.

### 4.4 Priority Ranking

Ranked by relevance to ThisIsMyPC's current implementation priorities:

| Priority | EP Capability | ThisIsMyPC Relevance | Action |
|----------|--------------|---------------------|--------|
| **1** | Context menu architecture, how modern vs classic menus work, CLSID `{86ca1aa0}`, `ImmersiveContextMenuHelper` pipeline | Stories 2.7-2.10, Epic 27 | Study dllmain.c:2400-2700 and TwinUIPatches.cpp for API patterns |
| **2** | Registry manipulation patterns, HKCU vs HKLM, enforcement avoidance, settings monitoring | Epic 2 (registry probe), Epic 26 (enforcement) | Study registry hook implementations in dllmain.c:8452-8502, 10891-10900 |
| **3** | COM interception architecture, CoCreateInstance hooking, proxy wrapping, CFG bypass | Epic 2 (COM enumeration), Architecture | Study dllmain.c:8364-8447 for COM proxy patterns |
| **4** | Shell COM interfaces, which CLSIDs control what Explorer behavior | Stories 2.7-2.10 | Catalog CLSIDs from dllmain.c:8178-8198 and settings.reg |
| **5** | Symbol resolution, PDB parsing, pattern scanning for undocumented function discovery | Future: advanced probe capabilities | Reference only, our probe reads state, doesn't hook functions |
| **6** | File Explorer command bar control, UIRibbonFramework, XamlIslandViewAdapter | Epic 27 (Windows Annoyances) | Study ExplorerFrame_CoCreateInstanceHook at dllmain.c:8178 |
| **7** | Taskbar hooking patterns | Low priority | Reference for general Win32 hooking patterns |
| **8** | DLL injection techniques | Not applicable | We don't inject into other processes |

### 4.5 Key Takeaways for ThisIsMyPC

1. **Context menu restoration works at the presentation layer, not the namespace layer.** EP strips `MFT_OWNERDRAW` from menu items and lets Win32 render them classically. This means our probe doesn't need to understand the full IContextMenu pipeline to detect whether classic menus are active, we can check the registry CLSID override at `HKCU\Software\Classes\CLSID\{86ca1aa0-...}\InprocServer32` and/or check for EP's `DisableImmersiveContextMenu` setting.

2. **CFG blocks vtable patching in modern Windows processes.** EP's workaround (COM proxy objects) validates our architecture research. Our NativeAOT binaries will have CFG enabled, and we should design our COM interactions accordingly.

3. **HKCU registry writes avoid most enforcement mechanisms.** EP writes almost everything to `HKCU\Software\ExplorerPatcher`, avoiding UCPD, GPCache, and TrustedInstaller. Our enforcement layer should note this, HKCU is the path of least resistance for user-scoped settings.

4. **Symbol-based discovery is EP's resilience strategy.** We don't need this for our probe (we read documented registry paths), but it's worth understanding for future advanced capabilities.

5. **EP's `settings.reg` template** (ep_gui/resources/settings.reg) is a goldmine for mapping which registry values control which Explorer behaviors. Cross-reference this with our probe's registry path inventory.

---

## 5. Cross-Reference: EP CLSIDs & Registry vs ThisIsMyPC Stories

### 5.1 CLSID Catalog

#### Context Menu & Handler CLSIDs

| CLSID | What It Is | EP Usage | Our Story | Our Status |
|-------|-----------|----------|-----------|------------|
| `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` | Win11 modern context menu (IExplorerCommand host) | EP writes empty `InprocServer32` at HKCU to disable | **2.3** | **Done**, we toggle this CLSID in `ShellRegistryPaths.ClassicContextMenuKeyPath` |
| `{056440FD-8568-48e7-A632-72157243B55B}` | Secondary modern context menu handler | EP writes empty `InprocServer32` at HKCU when `bDisableImmersiveContextMenu=TRUE` (dllmain.c:3504) | **2.3, 2.10** | **Gap**, we only toggle `{86ca1aa0}`. This second CLSID may need to be toggled too for complete classic menu restoration. |
| `{d93ed569-3b3e-4bff-8355-3c44f6a52bb5}` | Win11 File Explorer Command Bar (modern toolbar) | EP blocks/enables based on `dwFileExplorerCommandUI` setting. Empty `InprocServer32` = block modern toolbar → falls back to classic command bar. (dllmain.c:5827, GUI.c:501) | **2.4, 27.x** | **Gap**, we don't expose command bar/ribbon toggle yet. EP shows the exact CLSID and mechanism. |
| `{2cc5ca98-6485-489a-920e-b3e88a6ccce3}` | Windows Spotlight (desktop background widget) | EP reads/writes `ShellFolder\Attributes` to hide icon; intercepts registry writes to suppress menu items via `dwSpotlightDesktopMenuMask` bitmask. (dllmain.c:6805-6808, 8891) | **27.1-27.3** | **Gap**, Spotlight suppression not yet planned in detail. EP provides the exact registry manipulation pattern. |
| `{1d64637d-31e9-4b06-9124-e83fb178ac6e}` | Archive file handler placeholder | EP maps via `TreatAs` to `{64bc32b5-4eec-4de7-972d-bd8bd0324537}` when `bEnableArchivePlugin=TRUE`. (GUI.c:591-594) | **2.7** | **Informational**, shows how `TreatAs` COM redirection works for static verb registration. |
| `{1eeb5b5a-06fb-4732-96b3-975c0194eb39}` | Classic theme shell extension | EP disables via empty `InprocServer32` when `bClassicThemeMitigations=TRUE`. (settings.reg:744) | **, ** | Not relevant to our current scope. |

#### Control Panel Navigation CLSIDs

| CLSID | What It Is | EP Usage | Our Story |
|-------|-----------|----------|-----------|
| `{BB06C0E4-D293-4F75-8A90-CB05B6477EEE}` | System (Control Panel) | `bDoNotRedirectSystemToSettingsApp` prevents Settings app redirect | **27.x**, potential annoyance toggle |
| `{7B81BE6A-CE2B-4676-A29E-EB907A5126C5}` | Programs and Features | `bDoNotRedirectProgramsAndFeaturesToSettingsApp` | **27.x**, potential annoyance toggle |
| `{D450A8A1-9568-45C7-9C0E-B4F9FB4537BD}` | Installed Updates | Related to Programs and Features redirect | **27.x** |
| `{17CD9488-1228-4B2F-88CE-4298E93E0966}` | Default Programs | Control Panel redirect | **27.x** |
| `{8E908FC9-BECC-40F6-915B-F4CA0E70D03D}` | Network and Sharing Center | EP redirect control | **27.x** |

### 5.2 Registry Path Cross-Reference

#### Paths We Already Have (in ShellRegistryPaths.cs or implementation)

| Registry Path | Value | Our Constant | Story | Notes |
|--------------|-------|-------------|-------|-------|
| `HKCU\...\Explorer\Advanced` | `TaskbarAl` | In ExplorerSettingsReader | 2.3 | Done |
| `HKCU\...\Explorer\Advanced` | `TaskbarDa` | In ExplorerSettingsReader | 2.3 | Done (Widgets) |
| `HKCU\...\Explorer\Advanced` | `Hidden` | In ExplorerSettingsReader | 2.1 | Done |
| `HKCU\...\Explorer\Advanced` | `HideFileExt` | In ExplorerSettingsReader | 2.1 | Done |
| `HKCU\...\Explorer\Advanced` | `ShowSuperHidden` | In ExplorerSettingsReader | 2.1 | Done |
| `HKCU\...\Explorer\Advanced` | `SeparateProcess` | In ExplorerSettingsReader | 2.1 | Done |
| `HKCU\...\Explorer\Advanced` | `ShowSyncProviderNotifications` | In ExplorerSettingsReader | 2.1 | Done |
| `HKCU\...\Explorer\Advanced` | `LaunchTo` | In ExplorerSettingsReader | 2.1 | Done |
| `HKCU\...\Explorer\Advanced` | `NavPaneShowAllFolders` | In ExplorerSettingsReader | 2.4 | Done |
| `HKCU\...\Explorer\Advanced` | `NavPaneExpandToCurrentFolder` | In ExplorerSettingsReader | 2.4 | Done |
| `HKCU\...\Explorer\Advanced` | `UseCompactMode` | In ExplorerSettingsReader | 2.4 | Done |
| `HKCU\...\Classes\CLSID\{86ca1aa0-...}\InprocServer32` | `(Default)` | `ShellRegistryPaths.ClassicContextMenuKeyPath` | 2.3 | Done |
| 10 `HKCR\...\shellex\ContextMenuHandlers` paths | Various CLSIDs | In ContextMenuScanner | 2.2 | Done (COM handlers) |

#### Paths EP Reveals That We Don't Have Yet

| Registry Path | Value | EP Setting | Story | Action |
|--------------|-------|-----------|-------|--------|
| `HKCU\...\Classes\CLSID\{056440FD-...}\InprocServer32` | `(Default)` | `bDisableImmersiveContextMenu` | **2.3** | **Add to ShellRegistryPaths**, second CLSID for complete classic menu toggle |
| `HKCU\...\Classes\CLSID\{d93ed569-...}\InprocServer32` | `(Default)` | `dwFileExplorerCommandUI` | **2.4 / 27.x** | **Future story**, command bar/ribbon style toggle |
| `HKCU\...\Classes\CLSID\{2cc5ca98-...}\ShellFolder` | `Attributes` | `bDisableSpotlightIcon` | **27.1-27.3** | **Add when implementing Spotlight suppression** |
| `HKLM\...\Shell Extensions\Blocked` | `{CLSID}` |, (EP doesn't use this) | **2.10** | We already planned this; EP validates that per-user CLSID override is an alternative |
| `HKCR\*\shell`, `HKCR\Directory\shell`, etc. | Verb subkeys |, (EP doesn't enumerate these) | **2.7** | Static verb scan paths, EP doesn't do this, our deep-research docs cover it |
| `HKLM\...\PackagedCom\Package\...\Class\{CLSID}` | DLL path |, (EP doesn't enumerate these) | **2.8** | Modern handler paths, EP blocks CLSIDs but doesn't enumerate PackagedCom |
| `HKCU\...\Explorer\Advanced` | `Start_ShowClassicMode` | `dwStartShowClassicMode` | **, ** | Not in our current scope (Start menu) |
| `HKCU\...\Search` | `SearchboxTaskbarMode` | `dwSearchboxTaskbarMode` | **27.x** | Potential annoyance toggle, hide/show search box |
| `HKCU\...\Explorer` | `AltTabSettings` | `dwAltTabSettings` | **27.x** | Potential annoyance toggle, Alt+Tab style |

### 5.3 Gap Analysis by Story

#### Story 2.3: Classic Context Menu Toggle, 1 Gap Found

**Gap:** We only toggle `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}`. EP also disables `{056440FD-8568-48e7-A632-72157243B55B}` for complete classic menu restoration.

**Action:** Add `{056440FD}` as a second CLSID in `ShellRegistryPaths.cs`. Update the classic context menu `ChangeDescriptor` to toggle both CLSIDs atomically. Verify via testing whether `{056440FD}` suppression is actually needed when `{86ca1aa0}` is already disabled, EP may be belt-and-suspenders here.

#### Story 2.4: Explorer File-Browsing Preferences, 1 New Opportunity

**Opportunity:** EP exposes `dwFileExplorerCommandUI` which toggles between modern and classic command bar/ribbon via CLSID `{d93ed569-3b3e-4bff-8355-3c44f6a52bb5}`. This is a natural addition to our Explorer preferences module.

**Action:** Consider adding as a preference in Story 2.4 or as a new story under Epic 27. The mechanism is identical to our classic context menu toggle, write empty `InprocServer32` to block the modern CLSID.

#### Story 2.7: Static Verb Enumeration, EP Confirms, Doesn't Extend

EP doesn't enumerate static verbs. Its verb-related code is limited to the archive handler `TreatAs` mechanism and the Win+X Properties item. Our deep-research docs (cm, cm2, cm3) remain the primary reference for static verb enumeration.

**No gaps from EP.** EP confirms that static verbs are a separate domain from COM handler management.

#### Story 2.8: Modern Packaged Handler Enumeration, EP Confirms Bifurcation

EP blocks modern handlers by CLSID (`CLSID_FileExplorerFolderView`, `CLSID_XamlIslandViewAdapter`) but does not enumerate them. This confirms the modern/legacy bifurcation documented in our cm2/cm3 research.

**No new information from EP.** Our `AppExtensionCatalog` approach from cm3 research remains the correct enumeration strategy.

#### Story 2.10: Blocked List, EP Uses Alternative Approach

EP uses per-user CLSID `InprocServer32` override (HKCU) rather than the system-wide Blocked list (HKLM). Both approaches work:
- **HKCU override** (EP's approach): No elevation needed, user-scoped, but only works for COM in-proc servers
- **HKLM Blocked list** (our planned approach): Requires elevation, system-wide, works for all shell extension types

**Insight:** We should support both mechanisms. HKCU override for non-elevated mode, HKLM Blocked list when elevated. EP validates that the HKCU approach is effective.

#### Stories 27.1-27.3: Windows Annoyances, EP Adds Several Candidates

EP exposes settings we haven't cataloged for Epic 27:

| EP Setting | What It Does | Story Fit | Effort |
|-----------|-------------|-----------|--------|
| `SearchboxTaskbarMode` (0/1/2) | Hide/show/icon-only search box | 27.2 (Bing Search) | Low, single DWORD |
| `AltTabSettings` (0/1) | Win11 vs Win10 Alt+Tab | 27.4 (Gaming/Accessibility) | Low, single DWORD |
| `bDoNotRedirectSystemToSettingsApp` | Keep Control Panel for System | New sub-story | Low, single DWORD |
| `bDoNotRedirectProgramsAndFeaturesToSettingsApp` | Keep Control Panel for Programs | New sub-story | Low, single DWORD |
| `bDoNotRedirectDateAndTimeToSettingsApp` | Keep Control Panel for Date/Time | New sub-story | Low, single DWORD |
| File Explorer Command Bar (`{d93ed569}`) | Classic vs modern command bar | New sub-story or 2.4 | Low, CLSID toggle |
| Spotlight suppression (`{2cc5ca98}`) | Hide Spotlight desktop icon/menu | 27.1 | Medium, bitmask + Attributes |

**Note:** The "Do not redirect to Settings app" toggles are a natural fit for Epic 27, they're zero-enforcement, single-DWORD registry writes that restore classic Control Panel behavior.

### 5.4 Summary: What EP Gives Us

| Category | Value Added | Priority |
|----------|-----------|----------|
| **Second context menu CLSID** (`{056440FD}`) | Completes our classic menu toggle | High, verify and add to 2.3 |
| **Command bar CLSID** (`{d93ed569}`) | New toggle for Explorer preferences | Medium, add to 2.4 or 27.x |
| **Spotlight suppression pattern** | Desktop icon/menu bitmask control | Medium, add to 27.1 |
| **Control Panel redirect toggles** | 4 new annoyance settings | Low, add to 27.x |
| **Search box mode** | Taskbar search visibility | Low, add to 27.2 |
| **HKCU CLSID override as disable mechanism** | Alternative to HKLM Blocked list | Informational, consider dual-path in 2.10 |
| **Alt+Tab settings** | Win11 vs Win10 switcher | Low, add to 27.4 |
| **CFG proxy pattern validation** | Confirms our architecture research | Informational, no code change |
| **Static verb / PackagedCom** | EP doesn't cover these, our research docs remain primary | No gap filled |

---

*Last updated: 2026-03-08*
*Analysis performed by: Claude Code (Opus 4.6) across 4 parallel analysis agents*
*Total codebase analyzed: ~25,000 lines of C/C++ across 11 projects*
