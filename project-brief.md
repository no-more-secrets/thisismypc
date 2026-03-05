# Project Brief: ThisIsMyPC

## Project Name

**ThisIsMyPC** — A unified Windows system control application

## Problem Statement

Windows power users currently juggle a fragmented ecosystem of system control tools. Adjusting monitor brightness requires ddcutil or a vendor app. Controlling RGB lighting means OpenRGB. Managing ASUS laptop features requires G-Helper. Cleaning up startup items means Autoruns. Fan curves, power profiles, GPU MUX switching, service management — each one lives in a separate tool with its own UI paradigm, update cycle, and level of polish.

This fragmentation creates real friction:

- Users must discover, install, and maintain 5–10+ separate utilities
- No unified view of system state — hardware settings are scattered across tools
- Vendor software (Armoury Crate, iCUE, Synapse) is bloated, telemetry-heavy, and often conflicts with other tools
- Many power-user features are buried behind WMI/ACPI calls with no GUI at all
- There is no single tool that says: "This is YOUR PC — here's everything, in one place"

## Vision / Solution

ThisIsMyPC is a GPLv2-licensed Windows application that consolidates system control into a single, modern interface. It provides direct hardware access for display control (DDC/CI), RGB lighting, platform-specific tuning (ASUS WMI/ACPI, and potentially other vendors), startup/service management, and more — all without the bloat, telemetry, or vendor lock-in of OEM software.

The application is built with .NET and Avalonia UI for a modern, responsive frontend, backed by C++ native modules for low-level hardware interaction via P/Invoke or C++/CLI interop.

### Core Philosophy

- **Ownership**: Your hardware, your rules. No telemetry, no cloud dependency, no artificial limitations.
- **Transparency**: Open source (GPLv2) — users can audit, modify, and contribute.
- **Consolidation**: One app replaces many. Unified UI, unified configuration, unified control.
- **Modularity**: Plugin-based architecture so hardware support can grow organically.

## Target Users

- **Primary**: Windows power users who currently run multiple system utilities (enthusiasts, overclockers, sysadmins, developers)
- **Secondary**: ASUS ROG hardware owners looking for a lighter, open-source alternative to Armoury Crate
- **Tertiary**: Anyone frustrated with vendor bloatware who wants clean, direct hardware control

## Key Features (MVP Scope)

### Module 1: Display Control (DDC/CI)

- Brightness, contrast, and input source switching for external monitors
- Per-monitor profiles
- Hotkey support

### Module 2: Startup & Services Management

- Autoruns-style visibility into startup items, scheduled tasks, and services
- Enable/disable/delay startup entries
- Risk indicators for unknown or suspicious entries

### Module 3: System Information Dashboard

- At-a-glance view of CPU, GPU, RAM, storage, temperatures, and fan speeds
- Real-time monitoring with lightweight resource usage

### Module 4: Platform-Specific Tuning (Stretch Goal for MVP)

- ASUS WMI/ACPI integration: fan curves, performance profiles, GPU MUX switch
- Extensible to other vendors (MSI, Lenovo, etc.) via plugin interface

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | Avalonia UI (.NET) |
| Core Logic | .NET 8+ (C#) |
| Native Modules | C++ with C++/CLI or P/Invoke interop |
| Hardware APIs | Win32, WMI, ACPI, I2C/DDC, HID |
| Build System | MSBuild / CMake (for native) |
| License | GPLv2 |

## Architecture Overview

```
┌─────────────────────────────────────┐
│          Avalonia UI (XAML)          │
│      Dashboard / Module Views       │
├─────────────────────────────────────┤
│        .NET Core Library            │
│   Module Interface / Plugin Host    │
│   Settings / Profiles / Hotkeys     │
├──────────┬──────────┬───────────────┤
│ DDC/CI   │ Startup  │ ASUS WMI     │
│ Module   │ Module   │ Module       │
│ (C++)    │ (C#)     │ (C++)        │
├──────────┴──────────┴───────────────┤
│     Windows APIs / Drivers          │
│  Win32 · WMI · ACPI · I2C · HID    │
└─────────────────────────────────────┘
```

## Constraints & Considerations

- **Privilege model**: Many subsystems require elevated access. A Windows service running as SYSTEM with the UI communicating via named pipes or gRPC is the cleanest approach, though running the app elevated is acceptable for MVP.
- **GPLv2 compliance**: Required due to potential use of OpenRGB and ddcutil code/protocols. All contributions must be GPLv2-compatible.
- **Windows-only**: This is explicitly a Windows system control tool. No cross-platform considerations.
- **Hardware diversity**: DDC/CI and HID implementations vary wildly across manufacturers. Extensive testing and graceful fallbacks are essential.

## Success Metrics

- Replaces at least 3 separate tools for a typical power user
- Sub-2-second startup time
- <50MB RAM usage at idle (monitoring dashboard active)
- Community contributions within 6 months of public release
- Positive reception in r/ASUS, r/OpenRGB, and similar communities

## Risks

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Hardware compatibility issues across vendors | High | Modular design, community testing, graceful degradation |
| Elevated privilege security concerns | Medium | Principle of least privilege, service isolation, code audits |
| Scope creep from too many modules | Medium | Strict MVP definition, plugin architecture for post-MVP features |
| GPLv2 license friction for some contributors | Low | Clear contribution guidelines, CLA if needed |

## Inspiration & Prior Art

| Tool | What it does well | What ThisIsMyPC improves |
|------|------------------|------------------------|
| OpenRGB | Vendor-agnostic RGB control | Integrates RGB alongside other system controls |
| ddcutil | DDC/CI monitor control | Adds GUI, profiles, and hotkeys |
| G-Helper | ASUS laptop tuning without Armoury Crate | Extends beyond ASUS, adds modularity |
| Autoruns | Deep startup/service visibility | Modernizes UI, integrates with system dashboard |
| HWiNFO | Comprehensive system monitoring | Adds control capabilities alongside monitoring |
