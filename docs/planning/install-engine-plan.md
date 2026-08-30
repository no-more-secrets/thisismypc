# Install/Uninstall Engine — Chapter Plan

Retired Epic 24 (FR109-111) built on CTT winutil's mature install feature (MIT).
Reference clone lives outside the repo; port data and recipes, credit winutil in
the catalog file header. Decision (Sam, 2026-08-30): staged batch UX — installs
queue like pending changes and run on Apply, not per-row immediate buttons.
Uninstalls route through each app's own uninstaller via `winget uninstall`.

## Design facts

- Installs/uninstalls are one-way: they cannot join the pending-changes pipeline
  (`ChangeDescriptor` requires `BeforeValue`; history assumes undo). A parallel
  **pending-actions queue** carries them: same staged/review/Apply rhythm, items
  marked one-way with an `UndoHint` (e.g. "Reinstall from the Microsoft Store").
- winutil's engine is data + a thin process call: `winget install --id X
  --accept-package-agreements --accept-source-agreements --source winget
  --silent` (msstore: prefix switches source). Port the flag recipe, not the
  PowerShell.
- Catalog data to port: `config/applications.json` (232 apps, 10 categories,
  winget IDs, descriptions, links, FOSS flags) and `config/appx.json` (33
  removable inbox apps with PackageId + StoreId; StoreId enables reinstall).
- `IAppxPackageService` (Core/Packages, impl Interop.Com) is already built,
  tested, and DI-registered with zero consumers — the Appx removal backend.
- Installed detection: `winget export` emits JSON (no fragile table parsing).

## Chunks

1. **Core action queue** — `Core/Actions/ActionDescriptor` + `ActionBatchResult`,
   `IPendingActionsService`/`PendingActionsService` (sequential,
   continue-on-failure, succeeded actions leave the queue, failed ones stay),
   `IActionModule : IModule` with `ExecuteActionAsync`. Tests mirror
   PendingChangesService tests.
2. **App integration** — review panel shows a one-way Actions section; Apply
   runs changes then actions; routing via `ResolveModule` + `IActionModule`.
3. **Winget interop** — `IWingetService` in Core (availability, list installed
   via export, install, uninstall; `OperationResult<T>`, exit-code mapping),
   impl in `Interop.Win32/Packages/` over Process.
4. **Modules.Software project** — catalog as embedded JSON (source-gen
   serializer context, NativeAOT-safe), `SoftwareModule` (availability = winget
   present), scan = catalog x installed-state join.
5. **UI** — SoftwareView custom view: search, category filter, checkbox staging
   into the action queue; per-run status from the queue's CurrentActionDisplay.
6. **Windows Apps section** — appx.json port; remove/deprovision via
   `IAppxPackageService`; reinstall via `winget install --source msstore
   <StoreId>` as the undo path.

Later (post-v1): update management (`winget upgrade`), OneDrive/Edge removal
recipes, OEM tools (G-Helper/OmenMon), SKU-upgrade-via-generic-keys feature.

## Status

- [ ] 1 Core action queue
- [ ] 2 App integration
- [ ] 3 Winget interop
- [ ] 4 Modules.Software + catalog
- [ ] 5 UI
- [ ] 6 Windows Apps (Appx)
