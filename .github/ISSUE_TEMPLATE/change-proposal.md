---
name: Change proposal
about: A new setting, module, set definition, or behavior change. Agree the approach here before code.
title: ""
labels: proposal
---

Start with the behavior you want. Your coding agent can help with technical details.
Leave a technical field as "unknown" if you cannot answer it yet.

## Module

Name the page this belongs to, or say "new page".

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
