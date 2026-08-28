# Tweak Set JSON Schema

Set definitions are JSON files loaded by `ISetProvider` (`Core/Sets/SetProvider.cs`) from:

- **Built-in sets**: `<install dir>\sets\` — bundled with the application
- **User sets**: `%APPDATA%\ThisIsMyPC\sets\` — user-created (Story 8.5) or hand-copied

Property names are camelCase (matching is case-insensitive). Comments (`//`) and trailing
commas are tolerated. Unknown properties are ignored, so newer files degrade gracefully on
older app versions. A file that fails to parse, or is missing any required field, is
skipped with a warning — it never breaks loading of other sets.

## Top-level object

| Property | Type | Required | Notes |
|---|---|---|---|
| `name` | string | yes | Display name, also the set's identity in the browser |
| `description` | string | yes | What the set does, shown in the set browser |
| `category` | string | yes | `"TweakSet"` (focused bundle) or `"OptimizationPack"` (comprehensive bundle) |
| `version` | string | yes | Author-managed, e.g. `"1.0.0"` |
| `author` | string | yes | e.g. `"ThisIsMyPC"` for built-ins |
| `entries` | array | yes, non-empty | See below. One invalid entry invalidates the whole file (partial sets are unsafe) |

## Entry object

| Property | Type | Required | Notes |
|---|---|---|---|
| `moduleId` | string | yes | Target module — the module's `IModule.Info.Name` string, e.g. `"Explorer"`, `"Windows Annoyances"`, `"Context Menus"` |
| `settingId` | string | yes | The module's setting key, e.g. `"taskbar-widgets"` |
| `value` | string | yes | Desired value, string-typed exactly like `ChangeDescriptor.AfterValue`. The module knows the value type; before-values are captured from the live system at staging time and are never stored in set files |
| `description` | string | yes | Human-readable explanation shown in the preview |
| `displayValue` | string | no | Human-readable rendering of `value`, e.g. `"Hidden"` |
| `group` | string | no | Constituent-set label; optimization pack previews group entries by this |
| `enforcement` | object | no | See below |

## Enforcement object (mirrors `SettingEnforcement`)

| Property | Type | Notes |
|---|---|---|
| `companionServices` | string[] | Services stopped + disabled around the mutation (e.g. `["DiagTrack"]`) |
| `companionTasks` | string[] | Scheduled task paths (not yet supported by the executor — entries carrying this fail to apply with a clear error) |
| `gpCacheEntries` | string[] | GPCache sync targets (not yet supported, same behavior) |
| `reversionVectors` | string[] | Informational reversion risks shown to the user, e.g. `["Windows feature updates"]` |
| `skuRestriction` | string | `"Home"`, `"Pro"`, `"Enterprise"`, or `"Education"` — the SKU the entry is blocked/cosmetic on |
| `ownerModeRequired` | bool | Requires the Owner Mode service (v1.0) |
| `aclElevation` | bool | Requires registry ownership transfer (not yet supported) |

## Toggle value convention

For settings a module surfaces as a suppress/restore toggle, `value` is the **primary
registry value the module writes in the desired state** — the same string the module's
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
