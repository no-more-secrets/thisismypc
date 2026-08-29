# SKU Restriction Audit (Home/Pro/Enterprise/Education)

Which shipped tweaks are edition-dependent, per the **official Policy CSP edition
tables** (learn.microsoft.com, fetched 2026-08-29). These feed the
`SettingEnforcement.SkuRestriction` tags (informational only, FR129 — the write always
succeeds and is undoable; the tag drives the card callout and set-preview notice).

## How to read this

The CSP tables describe *managed policy* support (Group Policy / MDM). They are
conservative: community reports say some of these registry values are honored on Home in
practice (e.g. TargetReleaseVersion). The tag semantics ("Windows ignores this on your
edition") match the official documentation, and the UI wording deliberately hedges.
`WindowsSku` restriction is single-valued — "restricted on Home AND Pro" is not
expressible (flagged since 26-3; first real case would be `AllowTelemetry=0`, which is
Enterprise/Education-only and not shipped yet).

## Tagged `SkuRestriction = Home`

| Module setting | Policy value(s) | CSP source (editions column: Pro/Ent/Edu/IoT only) |
|---|---|---|
| WU `version-pin` | TargetReleaseVersion, ProductVersion, TargetReleaseVersionInfo | [policy-csp-update](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update) |
| WU `auto-update-mode` | AUOptions (CSP: AllowAutoUpdate) | policy-csp-update |
| WU `no-auto-reboot` | NoAutoRebootWithLoggedOnUsers (legacy AU family; same policy processing as AllowAutoUpdate) | policy-csp-update (family inference — no dedicated CSP row) |
| WU `exclude-drivers` | ExcludeWUDriversInQualityUpdate | policy-csp-update |
| WU `delivery-optimization` | DODownloadMode | [policy-csp-deliveryoptimization](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-deliveryoptimization) |
| Annoyances `copilot` | TurnOffWindowsCopilot (deprecated by MS; User scope) | [policy-csp-windowsai](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai) |
| Annoyances `recall` | AllowRecallEnablement, DisableAIDataAnalysis | policy-csp-windowsai |
| Annoyances `activity-history` | EnableActivityFeed | [policy-csp-privacy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy) |

Note: several *other* Recall policies (deny lists, storage caps, data providers) are
Enterprise/Education-only — Pro is also excluded there. Not shipped; if they ever are,
the single-valued limitation bites.

## Audited and deliberately NOT tagged

| Setting | Why no tag |
|---|---|
| `bing-search` (DisableSearchBoxSuggestions, HKCU policy) | ADMX applicability lists Pro+, but consistent field reports confirm the HKCU registry value is honored on Home. A "Windows ignores this" notice would likely be false. Revisit if evidence changes. |
| `edge-shortcuts` (EdgeUpdate policies) | Applied by Edge's updater on every edition. |
| `edge-sidebar` (Edge HubsSidebarEnabled) | Edge browser policy honored on unmanaged devices of any edition (not on Edge's protected-policies list). |
| All HKCU preference values (ContentDeliveryManager, GameDVR, GameBar, accessibility, advertising-id, copilot-button, UserProfileEngagement) | User preferences, not policy-gated; edition-independent. |
| HAGS (GraphicsDrivers HwSchMode) | Driver/hardware gated, not edition gated. |
| Services / scheduled tasks / startup entries (Clean Boot) | SCM and Task Scheduler are edition-independent. |
| Shell / context-menu / power settings | Non-policy registry + powrprof — edition-independent. |
| Telemetry note | `AllowTelemetry=0` is respected only on Enterprise/Education (control-surface research L28). Relevant to the future Privacy & Telemetry module, and needs the multi-SKU restriction the current model can't express. |

## Where the tags live

- `WindowsUpdateChangeFactory.WUPolicyEnforcement` / `DOPolicyEnforcement`
- `AnnoyanceChangeFactory.CopilotDriftEnforcement` / `HomePolicyEnforcement`
  (+ `HomeRestrictedSingles` id table)
- Surfaced by: card callout (SettingCardViewModel, 10-3), set-preview notice
  (SetConflictResolver.BuildSkuNotice, 8-4). Never gated by the executor (FR129).
