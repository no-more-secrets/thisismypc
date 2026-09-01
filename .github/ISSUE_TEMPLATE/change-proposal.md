---
name: Change proposal
about: A new setting, module, set definition, or behavior change. Agree the approach here before code.
title: ""
labels: proposal
---

<!-- Written for an agent to fill. Every field is something the reviewer needs before saying yes. -->

## Module

Which module this belongs to (Explorer, Context Menus, Environment, Power
Plans, Startup & Services, Windows Annoyances, Windows Update, Privacy &
Telemetry, Software, Display), or "new module" with the group it would join
(Core, System, Hardware).

## The setting or behavior

What the user sees and what it controls.

## Registry or API surface

Exact keys, values, services, tasks, or APIs involved. Cite the source
(Microsoft docs, a ProcMon trace, an open-source tool's recipe with its
license).

## Before-state

How the current value is captured before the change, and how the revert
restores it. If the operation is one-way (install, uninstall), say so; it
goes through the actions queue instead.

## Enforcement

Does Windows revert this on its own (feature update, scheduled task, policy
cache, UCPD)? If yes, what keeps it applied. Which Windows SKUs it applies
to.

## Open decisions for the owner

Naming, placement in the UI, anything irreversible on a user machine.
