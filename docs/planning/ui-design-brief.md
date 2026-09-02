# ThisIsMyPC: UI design brief

For redesign work. Describes what every screen contains and how the app behaves,
not how it currently looks. Screenshots accompany this document; treat them as
the current state, not the target.

## What the app is

A Windows desktop app for people who want full control of their PC. It
consolidates trusted power-user utilities: debloat and privacy tweaks, startup
management, context menu cleanup, power plans, app install/update. The core
promise: every change captures its before-state and can be undone. The tone is
competent and calm, not gamer or enterprise. Users range from curious
tinkerers to sysadmins. It runs elevated and touches the registry, so trust and
legibility matter more than flash.

Two themes required, dark and light, equal citizens. Dark is the default.

## App skeleton

- Left sidebar: navigation. Collapsible. Groups: Core modules, System modules,
  Sets (Set Loader, Settings). Each module has an icon and name. Unavailable
  modules appear dimmed with a reason. A search box filters settings across all
  modules.
- Header above content: current module name (large display type) and a
  one-line description.
- Content area: the active module's view. Scrolls independently.
- Bottom apply bar, always visible: History button, Restore point button, a
  pending-changes summary ("3 changes, 2 actions" or "No pending changes"),
  status message with severity color, Discard and Apply buttons. Apply is the
  single commit point for everything staged anywhere in the app.
- Review panel: expandable from the apply bar. Lists every staged change
  grouped by module, with system location detail per change, remove buttons,
  a separate "one-way actions" section for installs/uninstalls (flagged as not
  undoable), and a "save as set" form. This is the moment of consent; it
  should feel like reading a receipt before signing.

## Recurring components

- **Toggle row**: label, wrapping description, optional warning text, optional
  monospace registry path, toggle switch on the right. The workhorse of the
  app; hundreds of instances. Rows show a pending state when staged (visually
  marked until applied) and a disabled state with an inline reason.
- **Setting card**: a richer row used by card-based modules. Groups of cards
  under a section header with a group description. Cards can carry: an SKU
  callout ("needs Pro"), an enforcement note, an expandable advanced section,
  and a per-card registry data panel toggled globally ("Registry Data" and
  "Compact" view toggles at the top of those modules).
- **Scope badges**: small tinted chips (Files, Folders, Background, Desktop,
  Misc) on rows that affect multiple right-click contexts.
- **Status colors**: success, warning, danger, info, each with a muted
  companion tint for backgrounds. Used for status text, warnings on rows,
  availability dots, badge tints.
- **Empty/degraded notices**: modules keep working when a probe fails and say
  so inline ("Installed state could not be read from winget").

## The screens

1. **Home**: first-launch capability summary (which modules this system
   supports, with green/amber dots and Open buttons), hardware ecosystem
   detections, later: recent activity and monitoring alerts. The landing page;
   currently the least designed.
2. **Explorer**: ~26 toggle rows in thematic groups (taskbar, Start menu, File
   Explorer behavior).
3. **Context Menus**: tab strip (Multi, File, Folder, Folder Background,
   Desktop, Misc, Windows, Per File Type, Custom) with counts. Rows are shell
   handlers: name, scope badges, publisher line, warning line for critical
   items, registry path. A global "Registry Info" toggle. Some rows are
   orphaned (broken handlers) and marked as such.
4. **Environment**: two variable tables (user, system) and a PATH editor with
   drag-to-reorder rows, inline add/edit, and validation.
5. **Power Plans**: plan cards (one active), a dense grid of per-plan settings
   (AC/DC columns), and a "System power" toggle section (hibernation,
   Ultimate Performance).
6. **Startup & Services**: laid out like Autoruns, one tab per category
   (Logon, Explorer, Services, Drivers, Scheduled Tasks, and the rest), a
   "Hide Microsoft entries" box, a search box that replaces the tabs with one
   list across every category while it has text, and a per-row switch that
   stages the change.
7. **Windows Annoyances**: ~30 setting cards in groups. The reference
   card-based module.
8. **Windows Update**: 8 setting cards.
9. **Privacy & Telemetry**: 8 setting cards, some with SKU callouts.
10. **Software**: three tabs. App Catalog: searchable list of ~230 apps with
    category filter, install/uninstall buttons that queue actions, Installed
    and Open source tags. Updates: available updates with version transitions
    and an Update all button. Windows Apps: built-in app removal with
    reinstall where possible.
11. **Set Loader**: bundled and custom tweak sets; each set previews its
    entries with current-state comparison before staging.
12. **Settings**: sectioned preference list (choice rows, toggle rows, text
    rows), settings export/import with a preview, Owner Mode service controls,
    and the capability report.
13. **Change History**: chronological applied-change groups with per-change
    detail and revert buttons.

## Fixed constraints

- Windows 11 desktop, resizable window, usable at 1280x800 and up.
- Framework is Avalonia (XAML). Anything expressible in modern CSS is broadly
  achievable: shadows, gradients, rounded corners, subtle animation. No web
  runtime.
- Density matters: this is a tool, most screens are long lists. Comfortable
  but not airy.
- Current fonts: IBM Plex Sans (body), IBM Plex Mono (registry paths and
  ids), a condensed display face for module titles. Open to change.
- Accessibility: WCAG AA contrast in both themes; an optional
  dyslexia-friendly font swap exists and must survive the design.
- No em dashes in any copy.

## What we want from the design pass

A distinctive visual system, not a Fluent/stock look: color tokens for both
themes, type scale, spacing and radius scale, elevation treatment, and the
signature look of the five workhorses (sidebar, toggle row, setting card,
apply bar, review panel). Deliver as reusable tokens and component specs so
they port cleanly to XAML.
