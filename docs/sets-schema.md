# Tweak Set JSON Schema

Set definitions are JSON files loaded by `ISetProvider` (`Core/Sets/SetProvider.cs`) from:

- **Built-in sets**: `<install dir>\sets\`, bundled with the application (`AppContext.BaseDirectory`)
- **User sets**: `%ProgramData%\ThisIsMyPC\sets\`, saved from the review panel or hand-copied. The root is `AppConstants.DataDirectoryPath` (machine-scoped data directory)

Property names are camelCase (matching is case-insensitive). Comments (`//`) and trailing
commas are tolerated. Unknown properties are ignored, so newer files degrade gracefully on
older app versions. A file that fails to parse, or is missing any required field, is
skipped with a warning: it never breaks loading of other sets.

## Top-level object

| Property | Type | Required | Notes |
|---|---|---|---|
| `name` | string | yes | Display name, also the set's identity in the browser |
| `description` | string | yes | What the set does, shown in the set browser |
| `category` | string | yes | `"TweakSet"` (focused bundle) or `"OptimizationPack"` (comprehensive bundle) |
| `version` | string | yes | Author-managed, e.g. `"1.0.0"` |
| `author` | string | yes | e.g. `"ThisIsMyPC"` for built-ins |
| `entries` | array | yes, non-empty | See below. One invalid entry invalidates the whole file (partial sets are unsafe) |
| `desktopOnly` | bool | no | Requires a verified desktop before staging. Unknown and portable form factors remain blocked. Defaults to false. |
| `appRemovals` | string[] | no | Windows app catalog IDs, reviewed as one-way actions. Empty IDs invalidate the file; unknown IDs cannot stage. Defaults to empty. |

App removals use the existing Software action queue. They never create before-values or undo history.
The preview enumerates packages for the current user and selects matching installed apps by default.
The existing removal action affects all users and deprovisions the app for new profiles.
The preview states this scope and the lack of undo. Inventory failure blocks the pack instead of reporting no installed apps.
Reinstall actions already in the queue block a conflicting removal. Older versions ignore these fields and cannot enforce desktop scope;
do not distribute desktop-only packs for use with older builds.

## Desktop intro guide

Home > Intro guide introduces every registered module, including unavailable modules.
Welcome introduces the tutorial. Each module uses its sidebar icon and gives concrete steps for trying its controls.
Draft additions belong in `docs/planning/tutorial-notes.txt`; saving that notebook does not change runtime lesson text.
Module availability is not a claim that its settings were scanned. Open module performs the existing module inspection.
Returning through Home resumes the lesson and selections during the current app session.
Opening a module from the guide shows Return to guide in the page header. It restores the same lesson and selections.
Navigating elsewhere clears that return button; Home > Intro guide still resumes the guide.

Desktop-only support applies to this guide's pack workflow, not the rest of the app.
Portable and unknown machines can read lessons but cannot open the pack from the guide or stage it in Presets.
The final Review pack button selects the bundled Desktop Grand Pack and carries excluded module recommendations forward.
The pack separately reviews live settings and recommended app removals; no lesson applies changes.

The pack contains all entries from Clean Boot, NukeCopilot, Privacy Baseline, and Windows Update Control.
It adds Widgets and the classic context menu from Windows 10-ify, plus optional Windows Search startup disabling.
There are 58 settings and five app removals: Clipchamp, News, Weather, Copilot, and Microsoft 365 Hub when installed.
The Search entry starts unchecked. It changes the service startup type, does not stop an already-running service,
and does not delete the index. Indexed file and Outlook searches are affected after the service stops or Windows restarts.
The update set pins the current feature release and selects notification before download; users must check updates and release support.
Defender engine removal remains a requested extension, not an implemented pack action. It needs a supported implementation and recovery tests.

Teaching references: Microsoft's [diagnostic data explanation](https://support.microsoft.com/en-us/windows/privacy/diagnostics-feedback-and-privacy-in-windows)
and [connected experiences explanation](https://support.microsoft.com/en-us/windows/experience/essential-services-and-connected-experiences-for-windows).
Search scope follows Microsoft's [search indexing documentation](https://support.microsoft.com/en-US/Windows/Experience/Performance-Optimization/search-indexing-in-windows).
Device, module, and removal claims follow the app's existing classifiers, availability checks, and action factories.

Remaining refinements: saved progress across app restarts, per-setting lessons within modules, richer live findings,
and a post-apply guided results page. First-pass entry is the persistent Home invitation.

## Entry object

| Property | Type | Required | Notes |
|---|---|---|---|
| `moduleId` | string | yes | Target module: the module's `IModule.Info.Name` string, e.g. `"Explorer"`, `"Windows Annoyances"`, `"Context Menus"` |
| `settingId` | string | yes | The module's setting key, e.g. `"taskbar-widgets"` |
| `value` | string | yes | Desired value, string-typed exactly like `ChangeDescriptor.AfterValue`. The module knows the value type; before-values are captured from the live system at staging time and are never stored in set files |
| `description` | string | yes | Human-readable explanation shown in the preview |
| `displayValue` | string | no | Human-readable rendering of `value`, e.g. `"Hidden"` |
| `group` | string | no | Constituent-set label; optimization pack previews group entries by this |
| `selectedByDefault` | boolean | no | Defaults to `true`. Set `false` for an optional entry that starts unchecked. Conflict and availability checks still apply. |
| `enforcement` | object | no | See below |

## Enforcement object (mirrors `SettingEnforcement`)

| Property | Type | Notes |
|---|---|---|
| `companionServices` | string[] | Services stopped + disabled around the mutation (e.g. `["DiagTrack"]`) |
| `companionTasks` | string[] | Scheduled task paths disabled around the mutation. Executed when a scheduled-task service is registered; without one the change fails its gate before any step runs (`App/Services/EnforcementExecutor.cs`) |
| `gpCacheEntries` | string[] | Registry keys recursively deleted before the primary write so Windows rebuilds them from the policy hive (e.g. `["HKLM\\SOFTWARE\\Microsoft\\WindowsUpdate\\UpdatePolicy\\GPCache"]`). Paths must be hive-rooted, at least three levels deep, and contain a `GPCache` segment: anything else fails the whole change before any step runs. Cleared again on revert; never restored (derived state) |
| `reversionVectors` | string[] | Informational reversion risks shown to the user, e.g. `["Windows feature updates"]` |
| `skuRestriction` | string | `"Home"`, `"Pro"`, `"Enterprise"`, or `"Education"`: the SKU the entry is blocked/cosmetic on |
| `ownerModeRequired` | bool | Requires the Owner Mode service (v1.0) |
| `aclElevation` | bool | Requires registry ownership transfer. Not supported: the executor gate fails the change |

## Toggle value convention

For settings a module surfaces as a suppress/restore toggle, `value` is the **primary
registry value the module writes in the desired state**: the same string the module's
change factory would put in `ChangeDescriptor.AfterValue`. For group toggles (one
settingId covering several registry values, e.g. `copilot`, `recall`,
`settings-suggested-content`, `bing-search` in Windows Annoyances), `value` is the
**first** value of the group as the module's reader lists it; the resolver maps it to the
whole group's suppress/restore state. Examples: `advertising-id` → `"0"`, `copilot` →
`"1"` (TurnOffWindowsCopilot), `recall` → `"0"` (AllowRecallEnablement),
`classic-context-menu` → `""` (empty InprocServer32 default enables the classic menu).

Entries targeting module-owned toggles should carry **no `enforcement` object**: the
module's change factory attaches the authoritative enforcement metadata when the change is
staged. Set-level enforcement is for entries whose target has no module factory (none of
the built-in sets need this today).

## Example

```json
{
  "name": "Privacy Baseline",
  "description": "Disables advertising ID, suggestions, and diagnostic data collection.",
  "category": "TweakSet",
  "version": "1.0.0",
  "author": "ThisIsMyPC",
  "entries": [
    {
      "moduleId": "Windows Annoyances",
      "settingId": "advertising-id",
      "value": "0",
      "displayValue": "Disabled",
      "description": "Stops apps from using your advertising ID for cross-app tracking."
    },
    {
      "moduleId": "Explorer",
      "settingId": "taskbar-widgets",
      "value": "0",
      "displayValue": "Hidden",
      "description": "Removes the Widgets button from the taskbar."
    }
  ]
}
```

The bundled built-in sets (`src/ThisIsMyPC.App/sets/`) are the reference corpus; an
automated test cross-checks every entry against the module inventories.
