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
| `moduleId` | string | yes | Target module, e.g. `"Explorer"`, `"Context Menus"` |
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
      "moduleId": "Annoyances",
      "settingId": "advertising-id",
      "value": "0",
      "displayValue": "Disabled",
      "description": "Stops apps from using your advertising ID for cross-app tracking.",
      "enforcement": {
        "reversionVectors": ["Windows feature updates"]
      }
    },
    {
      "moduleId": "Startup",
      "settingId": "svc-diagtrack",
      "value": "Disabled",
      "description": "Disables the Connected User Experiences and Telemetry service.",
      "enforcement": {
        "companionServices": ["DiagTrack"]
      }
    }
  ]
}
```
