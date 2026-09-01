<!-- Fill every line. The reviewer (a fresh-context agent, then the owner) checks exactly this list. -->

## What this changes

One paragraph: the module, the setting or behavior, and the registry or API
surface it touches.

Issue: #
Backlog item (docs/planning/refinement-backlog.md) this closes or adds:

## Checklist

- [ ] `dotnet test --filter "Category!=Integration&Category!=Diagnostic"` green
- [ ] Integration tests run locally, elevated, if the change touches the registry, services, or tasks
- [ ] UI change: sight harness screenshots attached below (from `artifacts/ui-shots/`)
- [ ] Every reversible change is a `ChangeDescriptor` with a real before-state, staged through `IPendingChangesService`
- [ ] One-way operations (install, uninstall, upgrade) go through `IPendingActionsService`
- [ ] No Win32, COM, or WMI calls in `ThisIsMyPC.Core`
- [ ] No new opaque third-party binaries; recipes ported into set definitions or modules
- [ ] No em dashes in code, comments, docs, or commit messages
- [ ] Fresh-context code review run and its findings fixed

## Screenshots

(UI changes only. Before and after.)

## Notes for the reviewer

Anything the agent was unsure about, anything left untested, and any
decision that belongs to the owner (naming, branding, irreversible behavior).
