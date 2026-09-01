# Research Plan: Autoruns & ShellExView Exports

**Created:** 2026-03-07
**Status:** Not started
**Context:** Epic 1 complete. Epic 2 prep done (registry research, Avalonia guide). These exports provide ground truth data from Sam's Windows 11 25H2 install.

---

## Objective

Extract structured, implementation-ready data from both exports to feed into story creation for Epic 2 (story 2.2 specifically) and Epic 3. The goal is validated data models, categorization schemes, and registry path maps — not raw dumps.

---

## Phase 1: ShellExView Export (Epic 2, Story 2.2)

**Input:** ShellExView HTML export (personal machine data, kept outside the repo)
**Feeds:** Story 2.2 (Context Menu Handler Management)
**Output:** `docs/research/context-menu-handlers-analysis.md`

### Research Tasks

1. **Parse and categorize all extensions by type** — Separate context menu handlers from other shell extension types (property sheet handlers, drag-drop handlers, icon handlers, etc.). Story 2.2 only manages context menu handlers; knowing what to skip is as important as knowing what to include.

2. **Extract context menu handler inventory** — For each context menu handler: CLSID, display name, DLL path, publisher/company, enabled/disabled state, and which HKCR location(s) it's registered under (matches the enumeration paths in epic2-registry-research.md).

3. **Classify as Microsoft vs third-party** — Microsoft-signed handlers need different UX treatment (warn before disabling, label as "Windows built-in"). Third-party handlers are the primary management target.

4. **Cross-reference against epic2-registry-research.md enumeration paths** — Validate that every handler found by ShellExView maps to one of the documented `HKCR\*\shellex\ContextMenuHandlers\` paths. Flag any handlers registered in unexpected locations.

5. **Identify safe-to-disable vs risky handlers** — Microsoft handlers like "Open With" or "Send To" should be flagged as system-critical. Third-party handlers (7-Zip, Adobe, etc.) are safe targets.

6. **Produce structured output** — Table of all context menu handlers with classification, ready for the create-story agent to reference.

---

## Phase 2: Autoruns Export (Epic 3)

**Input:** Autoruns CSV export (personal machine data, kept outside the repo)
**Feeds:** Epic 3 stories 3.1–3.4
**Output:** `docs/research/autoruns-analysis.md`

### Research Tasks

1. **Categorize entries by Autoruns Category column** — Map each category to the relevant Epic 3 story:
   - `Logon`, `Explorer`, `Sidebar` → Story 3.1 (Startup Entry Scanner) + 3.2 (Startup Management)
   - `Services` → Story 3.3 (Windows Services Management)
   - `Scheduled Tasks` → Story 3.4 (Scheduled Task Auditing)
   - `Boot Execute`, `Drivers`, `Codecs`, `Office Addins`, etc. → Document but likely out of scope for Epic 3

2. **Extract registry paths per category** — For each category, document the exact `Entry Location` patterns. These become the scan targets for the startup scanner in story 3.1.

3. **Classify Microsoft vs third-party per category** — Using the Company column. Count the split to inform UX decisions (how many entries will users typically see?).

4. **Analyze enabled/disabled distribution** — Understand what's already disabled on a typical install. This informs default UI state and "recommended" disable lists.

5. **Map to data model requirements** — From the CSV columns, derive what fields `StartupEntry`, `ServiceEntry`, and `ScheduledTaskEntry` records need. Cross-reference with the architecture doc's existing type definitions.

6. **Identify entry locations not yet in registry research** — Any registry paths in the Autoruns export that aren't documented in `windows-settings-registry-map.md` or `epic2-registry-research.md` need to be added (or flagged for ProcMon validation).

7. **Produce structured output** — Categorized breakdown with registry paths, entry counts, classification, and data model recommendations per Epic 3 story.

---

## Phase 3: Cross-Reference (both exports)

1. **Shell extensions overlap** — ShellExView and Autoruns both track shell extensions. Cross-reference to ensure consistency and identify any entries one tool catches that the other misses.

2. **Registry path completeness audit** — Compile all unique registry paths from both exports. Check every path against existing docs. Any undocumented path gets flagged for ProcMon validation before implementation.

3. **Produce output:** Add any new paths to `docs/windows-settings-registry-map.md` as `[RESEARCHED]` entries.

---

## Execution Notes

- Both files are large. Use selective reading (filter by Category for autoruns, by extension type for ShellExView) to avoid filling context.
- Research agents should write findings directly to output files, not return raw data.
- Output docs become inputs to create-story via the `INDEX_GUIDED` discovery on `docs/index.md`.
- Phase 1 and Phase 2 can run in parallel. Phase 3 depends on both completing first.
- Phase 1 is needed before Epic 2 story 2.2 creation. Phase 2 is needed before Epic 3 story creation.
