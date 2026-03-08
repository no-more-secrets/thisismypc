# ThisIsMyPC

**One app to rule them all. A lightweight, open-source Windows desktop application that consolidates system control into a single interface.**

Replace ASUS Armoury Crate, Autoruns, ShellExView, HWiNFO's UI, OpenRGB, ExplorerPatcher, FanControl, and the dozen other tools you've been juggling — with one app that actually respects your system.

---

## The Problem

You build a PC — the fun part — and then spend the next week in configuration hell.

Windows 11 ships with a centered taskbar, a neutered context menu, aggressive startup bloat, Widgets nobody asked for, and a growing list of decisions Microsoft made on your behalf. ASUS Armoury Crate eats 200MB updates, breaks fan profiles after firmware changes, and takes 15 seconds to open. The fix is cobbling together a dozen disconnected tools — Autoruns for startup entries, ShellExView to figure out what's cluttering your context menu, ExplorerPatcher for the shell, G-Helper for ASUS hardware, HWiNFO for sensors. And if you're lucky enough to have discovered that OpenRGB and FanControl exist — buried under pages of forum posts recommending the manufacturer's own firmware that barely works — you get to configure those separately too. Each tool solving one piece of the puzzle, none talking to each other.

The cumulative cost: **weeks per machine. Hundreds of hours cumulatively.** And the battle never ends, because every Windows update, every new software install, and every driver change threatens to undo your work.

The controls exist. They're just scattered across registry hives, buried in obscure APIs, and siloed in tools that don't talk to each other.

## The Solution

ThisIsMyPC unifies everything into a single pane of glass. One install. One interface. Every control surface your PC exposes, accessible without Googling registry paths or juggling separate apps.

### Core Modules

**Explorer** — Restore the Windows 10 context menu, move the taskbar left, disable Widgets, suppress tips and ads, manage Explorer preferences (hidden files, file extensions, folder views), and edit environment variables with a proper PATH editor.

**Context Menus** — The flagship module, and the one no other tool does well. Enumerate, visualize, and control every registered context menu handler on your system — see the source file, publisher, classification, and toggle them on or off across all registration surfaces. Tab-based UI organized by context menu surface (File, Folder, Folder Background, Desktop, Misc). Context menu creation and editing in v1.0.

**Startup & Services Management** — The "Autoruns replacement" angle. See every startup entry across registry run keys (including WOW6432Node mirrors and policy run keys), startup folders, Winlogon chains, and scheduled tasks — with file location, publisher, and description for each. Filter scheduled tasks by classification: telemetry, OEM bloatware, compatibility diagnostics, maintenance. Control Windows services: view state, change startup type, start/stop/restart. Covers all 200+ autostart extensibility points (ASEPs) that Windows provides.

**Power Plan Management** — View all power plans, switch the active plan, and adjust individual settings. Quick win, high daily-use value. Uses `powrprof.dll` directly — no WMI overhead.

**Display Control** — DDC/CI brightness, contrast, and input source switching for every connected monitor. No physical buttons needed. Uses `dxva2.dll` directly. Most users don't even know their monitors support software brightness control — this is a discovery moment.

**System Info Dashboard** — Real-time hardware sensors (CPU temp, GPU temp, fan speeds, utilization) via HWiNFO shared memory (`Global\HWiNFO_SENS_SM2`), system specs via WMI, and boot environment info. No driver required — reads the same shared memory that HWiNFO already exposes.

**ASUS Platform Tuning** — Full Armoury Crate replacement. Custom fan curves, GPU MUX switching (Optimus/dGPU), battery charge limit, CPU boost toggle, and performance profiles — all via the ATKACPI driver that ships with your chipset, not through Armoury Crate. Based on reverse-engineered patterns from G-Helper (ILSpy decompilation of the .NET binary). If you have an ASUS laptop or motherboard, this module means you never install Armoury Crate again.

**RGB Lighting** — Device control via an OpenRGB fork (GPLv2-compatible) with fallback to the OpenRGB SDK on TCP 6742. Set modes and colors per device, save named lighting profiles.

**Driver Management** — View all installed drivers with signing status, identify unsigned or third-party drivers, and disable automatic driver updates through Windows Update.

### Optimization Packs & Tweak Sets

Pre-built, opinionated recipes for common goals — organized in two layers:

**Tweak Sets** are focused, single-purpose bundles that target one specific annoyance. Examples:

- **NukeCopilot** — Disables all of Microsoft's Copilot AI features: the taskbar button, the shell integration, the background services. Everything Microsoft bolted on that wasn't there in Windows 10.
- **S32 Sticklers** — The services hiding in System32 that are totally unnecessary for your OS to function but stubbornly refuse to stay dead. GamingServices (Xbox overlay) is the poster child — disable it in Autoruns and it comes back next boot. ThisIsMyPC actually keeps it gone.
- **Privacy Baseline** — Disable advertising ID, activity history, diagnostic data, app suggestions, lock screen ads.

**Optimization Packs** are big bundles that compose multiple Tweak Sets (plus additional changes) into a comprehensive system overhaul:

- **Windows 10-ify** — Restore classic context menu, taskbar left, disable Widgets, classic Explorer navigation, suppress tips and suggestions — plus NukeCopilot, because Windows 10 didn't have any of these intrusive AI features. One click to undo two years of unwanted "upgrades."
- **Clean Boot** — S32 Sticklers plus non-essential startup entries and OEM bloatware services. The stuff that makes your boot take 45 seconds when it should take 15.

Everything stays transparent. Applying a pack or set stages all changes for review before committing — you see every registry key, every service, every scheduled task that will be touched. Individual changes can be deselected. Nothing writes until you say so.

Both layers plug into the existing module architecture and change tracking system. Community-contributed packs and sets are on the roadmap — same extensibility model as modules.

### PC Profiles

The defining feature that no other tool in this space offers.

Export your entire system configuration — every shell tweak, every startup preference, every power plan setting, every fan curve — as a single portable file. Build your perfect Windows state once, stamp it onto any fresh install. The profile adapts to what's present on the target machine: shell and startup settings apply universally, but Display Control skips if the monitors differ and ASUS Platform Tuning skips if there's no ASUS hardware. Skipped modules report what was skipped and why — no errors, no crashes, just clear feedback.

**The benchmark:** Fresh Windows install to fully personalized in under 2 hours. Subsequent machines with an existing profile: under 30 minutes.

Profiles export desired state, not mutation history. They're JSON, human-readable, diffable, and Git-friendly.

## Philosophy

### Zero Bloat by Default

The app that manages startup bloat must not *be* startup bloat.

Default install: no startup entry, no tray icon, no notifications, no background processes. Period. Close the app and check Task Manager — nothing. No scheduled tasks created. No registry keys written. No services installed.

Every persistent behavior is opt-in:
- **Minimize** minimizes to the taskbar (standard Windows behavior)
- **Close** kills everything (no "minimize to tray" surprise)
- **Tray mode** is opt-in only
- **Startup monitoring** (watch for new services infesting your registry) is opt-in only
- **Update checks** are opt-out — and the notification is a small unobtrusive badge, not a modal dialog

All three window behaviors (minimize, close, tray) are independently configurable. If you want ThisIsMyPC to live in your tray and watch for startup changes — it can. But only because you asked.

### Consent-Based Everything

No telemetry. No analytics. No cloud sync. No account system. No phone-home behavior of any kind. Fully offline by default. The only network touch point is the opt-out update check against GitHub Releases.

All data stays local: settings in `%APPDATA%\ThisIsMyPC\` as JSON files, change history in a local SQLite database. No remote logging. Ever.

### Every Change is Reversible

ThisIsMyPC tracks every system change it makes in a persistent change history with full undo/redo. Every mutation captures the before-state as a snapshot — the app rejects writes that don't include one. If a group of related changes partially fails, the successfully-applied changes are automatically rolled back. Change history survives across sessions.

You can open the change history weeks later and revert something you changed on day one.

### Show, Then Explain, Then Act

Every control displays what it does and where it lives in the system — the registry path, the WMI class, the service name, the scheduled task location. No mystery toggles. The "explain, don't obscure" principle is first-class content, not footnote text. Understanding is part of the value.

Advanced or potentially dangerous controls are available but behind a disclosure section. Nothing is hidden or locked behind "are you sure?" chains — but dangerous operations are clearly flagged.

## Why Not Just Use...?

| Tool | What it does well | What ThisIsMyPC adds |
|------|------------------|---------------------|
| **ASUS Armoury Crate** | Fan curves, GPU MUX, performance profiles, RGB | All of this without the bloatware. No 200MB updates, no 15-second launch, no broken firmware profiles. Uses the same ATKACPI driver that ships with your chipset. |
| **Autoruns** | Best-in-class ASEP coverage (200+ locations, 19 tabs) | Same coverage with a modern UI, scheduled task classification (telemetry vs. maintenance vs. OEM), and integration with the rest of your system config. Autoruns hasn't been meaningfully updated in years. |
| **HWiNFO** | Deepest hardware sensor coverage available | ThisIsMyPC reads HWiNFO's shared memory — you get the same sensor data in a unified dashboard without running a separate app. HWiNFO stays as the sensor engine; ThisIsMyPC is the UI. |
| **G-Helper** | Open-source ASUS control, lightweight | G-Helper is ASUS-only. ThisIsMyPC uses the same ATKACPI patterns but wraps them in a module alongside shell, startup, display, power, and RGB control. One app, not two. |
| **OpenRGB** | Broadest RGB device support | ThisIsMyPC integrates OpenRGB as a module (GPLv2 fork or SDK fallback). Same device support, unified with everything else. |
| **ExplorerPatcher** | Classic context menu, shell fixes | ThisIsMyPC covers the same shell customizations plus context menu handler management — something ExplorerPatcher doesn't touch and no standalone tool does well. |
| **FanControl** | Fan curve editor | Covered by the ASUS Platform Tuning module. Additional vendor support (MSI, Gigabyte, Lenovo) via community-contributed modules. |
| **NirSoft ShellExView** | Context menu handler enumeration | The closest thing to what ThisIsMyPC's Shell module does — but it looks like 2005, hasn't been updated for modern Windows, and doesn't integrate with anything else. |
| **Bulk Crap Uninstaller** | Deep uninstaller — catches leftover files, registry keys, and services that Windows "Apps & Features" misses | ThisIsMyPC doesn't replace BCUninstaller — it prevents you from needing it. Startup entries, services, scheduled tasks, and context menu handlers are all visible and controllable before they become entrenched. BCUninstaller cleans up after the damage; ThisIsMyPC stops the damage from landing. |
| **O&O ShutUp10++** | Privacy toggles, telemetry control | Phase 2 scope — ThisIsMyPC will cover privacy, telemetry, debloating, and Windows feature management. |

The point isn't that any individual capability is novel. **Unification is the feature.** The value is in consolidating 10+ tools into one coherent experience where settings are tracked, changes are reversible, and your entire configuration is exportable.

## Architecture

### Tech Stack

- **.NET 10 (LTS)** + **C# 14** — released November 2025, supported through ~2028
- **Avalonia UI 11.3.x** — cross-platform UI framework used here for Windows-only. Hardware-accelerated rendering, sub-1s cold start, NativeAOT support since 11.1
- **NativeAOT compilation** — single-file self-contained binary. No .NET runtime dependency for end users. Trimmed, ahead-of-time compiled
- **CommunityToolkit.Mvvm** — source-generated ObservableProperty, RelayCommand. Minimal MVVM boilerplate
- **CsWin32 (Microsoft.Windows.CsWin32)** — source-generated P/Invoke bindings from NativeMethods.txt declarations. SafeHandle support, friendly overloads
- **C++ native modules** — via P/Invoke for OpenRGB fork integration. CMake build
- **SQLite (Microsoft.Data.Sqlite)** — change history persistence. No ORM, no EF Core
- **Serilog** — structured logging to rolling file. NativeAOT-compatible sinks only
- **Velopack** — installer and delta updates via GitHub Releases
- **xUnit** — unit and integration testing

### Driver-Free Architecture

The MVP ships without any custom kernel drivers. Every system interaction uses documented user-mode APIs:

| API Surface | Used For |
|-------------|----------|
| **Win32 P/Invoke** | Service Control Manager (SCM), `powrprof.dll` (power plans), `dxva2.dll` (DDC/CI monitor control), SetupAPI (device enumeration) |
| **COM Interop** | `ITaskService` (scheduled tasks), shell extension handlers (context menu enumeration) |
| **WMI** | System information (`Win32_Processor`, `Win32_VideoController`), ASUS ATKACPI (`AsusAtkWmi_WMNB` via `root\WMI`) |
| **Shared Memory** | HWiNFO sensor data (`Global\HWiNFO_SENS_SM2`) |
| **ETW** | Real-time system event monitoring (registry changes, process creation) — kernel-level visibility from user mode |
| **Registry** | Direct read/write for shell customization, startup management, system preferences |

PawnIO driver integration (for direct hardware access beyond what user-mode APIs provide) is planned for Phase 2 as a pluggable backend — the abstraction layer exists from day one so driver-based implementations can replace driver-free ones without rewriting module logic.

### Solution Structure

```
ThisIsMyPC.sln
src/
  ThisIsMyPC.App/              — Avalonia host app (UI shell, module loading, navigation)
  ThisIsMyPC.Core/             — Module interface contract, shared types, change history engine
  ThisIsMyPC.Interop.Win32/    — P/Invoke wrappers via CsWin32 (SCM, powrprof, DXVA2, SetupAPI)
  ThisIsMyPC.Interop.Com/      — COM interop (ITaskService, shell extension handlers)
  ThisIsMyPC.Interop.Wmi/      — WMI query abstractions (system info, ASUS ATKACPI)
  ThisIsMyPC.Modules.Shell/    — Explorer + Context Menus modules (ShellModule + ContextMenuModule)
  ThisIsMyPC.Modules.Startup/  — Startup & Services Management module
  ThisIsMyPC.Modules.Power/    — Power Plan Management module
  ThisIsMyPC.Modules.Display/  — Display Control module (v1.0)
  ThisIsMyPC.Modules.Sensors/  — System Info Dashboard module (v1.0)
  ThisIsMyPC.Modules.Asus/     — ASUS Platform Tuning module (v1.0)
  ThisIsMyPC.Modules.Rgb/      — RGB Lighting module (v1.0)
  ThisIsMyPC.Modules.Drivers/  — Driver Management module (v1.0)
tests/
  ThisIsMyPC.Core.Tests/
  ThisIsMyPC.Modules.Shell.Tests/
  ThisIsMyPC.Modules.Startup.Tests/
  ThisIsMyPC.Modules.Power.Tests/
  ThisIsMyPC.Integration.Tests/
native/                        — C++ native modules (CMake — OpenRGB fork)
```

### Module Plugin System

Vencord-inspired architecture: the module interface is the product. First-party modules prove it works. Community modules extend it.

Modules implement a defined contract:
- **`ModuleInfo`** — name, icon, description, required system capabilities
- **`CheckAvailability()`** — reports whether the module can function on this hardware (e.g., "No ASUS ATKACPI driver detected")
- **`ScanSystemState()`** — reads the system, returns a data model (plain C# objects, no UI dependency)
- **`CreateChange()` / `ApplyChange()` / `RevertChange()`** — every mutation goes through the change tracking system with before-state snapshots

Modules are structurally isolated: no module-to-module references (enforced at build time). If two modules need the same system data, they both depend on the same interop layer. Module registration is explicit DI — no reflection-based scanning, which is required for NativeAOT compatibility.

Unavailable modules appear disabled with a reason, not hidden. A user on MSI hardware sees "ASUS Platform Tuning — No supported ASUS platform detected" — which is also an invitation for someone with MSI expertise to contribute a module.

### Admin Elevation

Single UAC prompt at launch. No split-process model. Users who decline elevation get a degraded experience — read-only scanning works, but all mutations (registry writes, service changes, task modifications) are disabled. This is the same model as Autoruns and FanControl. The alternative (an elevated helper service) adds significant architectural complexity for a power-user tool where elevation is the expected norm.

## Roadmap

### Beta (Current Target)

Three first-party modules that prove the architecture while delivering immediate utility:

1. **Explorer & Context Menus** — context menu control is the unique hook that no other tool provides. Expanded to cover all three handler types: dynamic COM handlers, static verb entries, and modern IExplorerCommand registrations.
2. **Startup & Services Management** — high daily-use value, the "Autoruns replacement" angle
3. **Power Plan Management** — quick win, validates the module interface end-to-end

Plus cross-cutting infrastructure that must land before module UI work:

4. **Security Hardening** — DLL search path hardening, data directory DACL, structured logging, update integrity, installation path enforcement. Pre-release blockers from threat modeling research.
5. **Enforcement-Aware Mutation Layer** — registry writes alone are insufficient for many Win11 settings. Kernel filter drivers (ucpd.sys), cloud policy caches (GPCache), TrustedInstaller ACL locks, and Defender Tamper Protection can silently revert changes. The enforcement layer models multi-step mutations (registry write + service disable + GPCache clear) with atomic rollback, and gates settings by Windows SKU.
6. **Module UI Template System** — card-based setting renderer with 4 display modes (default, registry data, compact, compact + registry data), Owner Mode degradation pattern, and enforcement-resistant setting indicators. Consumed by all modules.
7. **Windows Annoyances** — highest user-value, lowest effort. SCOOBE suppression, Bing search disable, Edge shortcut blocking, advertising/tracking toggles. The "download and get value in 30 seconds" features.

Plus the module plugin system, change history with undo/redo, settings persistence, zero-footprint defaults, and the Avalonia UI shell.

**Beta bar:** A power user can manage their shell, startup entries, and power plans from one app with full change tracking and undo support. Context menu handler control is the headline feature.

### v1.0

All 7+ modules functional. The "Armoury Crate replacement" release:

- **Display Control** — DDC/CI brightness, contrast, input switching
- **System Info Dashboard** — hardware sensors via HWiNFO shared memory
- **ASUS Platform Tuning** — fan curves, GPU MUX, battery limit, performance profiles
- **RGB Lighting** — device control via OpenRGB
- **Driver Management** — signing status, auto-update control
- **Session 0 Service & IPC** — SYSTEM-level background service with named pipe IPC for drift detection and future PawnIO integration. Anti-squatting, anti-impersonation, authenticated RPC.
- **Configuration Drift Watchdog** — post-reboot detection of settings that Windows silently reverted. Elevated from Phase 3 — without reapplication after updates, the core value proposition erodes.
- Opt-in tray mode, opt-out update check notifications
- Context menu creation and editing (graduated from beta's enumerate-and-toggle)
- System restore point creation before bulk changes
- Formalized plugin API with documentation for community module development

**v1.0 bar:** A power user can uninstall Armoury Crate, Autoruns, standalone DDC/CI tools, and OpenRGB's UI — and not miss them.

### Phase 2 — Growth (v1.x)

- **PC Profiles** — exportable system-wide configuration presets with cross-machine portability
- **Startup monitoring** — background watcher for new startup entries and services (opt-in)
- **Privacy & Telemetry** — diagnostic data, activity history, advertising ID, Copilot, Recall settings, recommended privacy presets
- **Windows Update Control** — status, pause, active hours, defer feature updates
- **Network Adapter Management** — DNS settings, IPv6 toggle, adapter control
- **Firewall Management** — view, enable/disable, and create firewall rules
- **PawnIO integration** — driver-based sensor backends for direct hardware access
- **Additional vendor support** — MSI, Gigabyte, Lenovo platform tuning (community-contributed modules)
- **Community plugin ecosystem** — third-party modules built against the formalized API

### Phase 3 — Vision

- **System Policy & Privacy expansion** — debloating, Windows feature management, privacy hardening (winutil / O&O ShutUp10++ territory)
- **Community profile library** — shareable configuration presets
- **Continuous drift monitoring** — real-time alerts when Windows updates or software installs change your preferences (drift detection core is v1.0; continuous monitoring is Phase 3)
- **Software Installation Engine** — curated catalog, package management (Linux package manager UX for Windows)

## Project Status

**In development** — foundational architecture shipped, implementing Beta modules.

- [x] Domain research — Windows system control ecosystem, competitive landscape, API mechanisms
- [x] Technical research — registry locations, WMI classes, and API patterns for every planned module
- [x] Deep research — 136 pages across 6 documents covering threat modeling, kernel driver security, Win11 context menu architecture, control surface enforcement mapping, and NativeAOT user-mode runtime integrity (CFG/ACG/CIG) ([master summary](docs/deep-research/SUMMARY.md) | [full documents](docs/deep-research/index.md))
- [x] Product brief
- [x] PRD — 138 functional requirements across 21 categories, phased across Beta/v1.0/Phase 2+
- [x] UX design specification
- [x] Architecture decision document
- [x] Epics and stories — 28 epics across Beta/v1.0/Phase 2/Phase 3
- [x] **Epic 1: Project Foundation & Application Shell** — solution scaffold, module interface contract, navigation shell, pending changes service, change history with undo/redo
- [ ] **Epic 2: Explorer & Context Menus** — in progress (registry interop and context menu handler management shipped; taskbar, preferences, environment variables, static verbs, modern handlers, orphan detection, blocked list remaining)

## Contributing

ThisIsMyPC is designed from the ground up for community contribution. The module plugin architecture means you can add support for your hardware without touching existing code.

**The contributor story:** You have MSI hardware. The ASUS module is greyed out. You browse the repo, see the module interface pattern, fork it, create an MSI Platform Tuning module following the same structure, map MSI's WMI classes, and submit a PR with hardware test results. The plugin architecture made it possible for someone with MSI expertise but no prior codebase knowledge to contribute a complete feature.

Areas where contributions will have the most impact:
- **Vendor-specific modules** — MSI, Gigabyte, Lenovo, and other platform tuning implementations
- **Hardware compatibility** — testing and reporting across different hardware configurations
- **RGB device support** — extending the OpenRGB integration for additional devices
- **Documentation** — module development guides, hardware compatibility tables

The project follows a benevolent-dictator governance model. Community contributes hardware support and modules; the maintainer owns the architectural vision.

## License

[GPLv2](LICENSE) — every component, permanently. The architecture's security is provable, not secret. See [Why GPLv2](docs/why-gplv2.md) for the full rationale.

---

*ThisIsMyPC is the anti-bloatware. It's the first piece of software on your PC that genuinely practices what it preaches about respecting that it's YOUR PC.*
