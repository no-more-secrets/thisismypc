# ThisIsMyPC

A lightweight, open-source Windows desktop app that consolidates system control into a single interface. Replace Armoury Crate, Autoruns, HWiNFO's UI, OpenRGB, ExplorerPatcher, and a dozen other tools — with one app that respects your system.

## The Problem

You build a PC, install Windows 11, and spend the next week fighting it. The context menu is neutered. Startup entries multiply behind your back. ASUS Armoury Crate eats 200MB updates and breaks fan profiles. You end up with 10+ disconnected tools, each solving one piece of the puzzle. Every Windows update threatens to undo your work.

The controls exist — they're just scattered across registry hives, buried in obscure APIs, and siloed in tools that don't talk to each other.

## The Solution

ThisIsMyPC unifies everything into a single pane of glass:

- **Shell & Explorer Customization** — Context menu editing (no good tool exists for this), taskbar, widgets, Explorer preferences, notification suppression
- **Startup & Services Management** — Registry run keys, startup folders, scheduled task auditing, service control
- **Power Plan Management** — Plan switching and setting adjustment
- **Display Control** — DDC/CI brightness, contrast, input switching — no physical buttons needed
- **System Info Dashboard** — Hardware sensors via HWiNFO shared memory, system specs
- **ASUS Platform Tuning** — Fan curves, GPU MUX, battery limit, performance profiles — without Armoury Crate
- **RGB Lighting** — Device control via OpenRGB
- And more: driver management, environment variables, undo/redo history for every change

### PC Profiles (Planned)

Export your entire system configuration as a portable preset. Fresh install to fully personalized in under 2 hours. Subsequent machines in minutes. No other tool offers this.

## Philosophy

**Zero bloat by default.** No startup entry, no tray icon, no notifications, no background processes — unless you explicitly enable them. The app that manages startup bloat must not *be* startup bloat.

Every persistent behavior is opt-in. Minimize minimizes to taskbar. Close kills everything. Tray mode is opt-in. All three behaviors are configurable.

If you want ThisIsMyPC to watch for new services infesting your registry and notify you — it can. But only if you ask.

## Tech Stack

- .NET 10 (LTS) + Avalonia UI + C# 14, NativeAOT compilation target
- C++ native modules via P/Invoke
- Driver-free architecture: Win32, WMI, COM, DXVA2, SetupAPI
- Vencord-inspired module plugin system — community-extensible

## Status

**Pre-development** — planning and research phase. PRD complete, architecture and UX design in progress.

## License

GPLv2
