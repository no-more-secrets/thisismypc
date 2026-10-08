# Control correctness checks

## Keyboard shortcut batch, 2026-10-07

Sticky Keys and Filter Keys previously compared complete Flags strings with fixed recipes.
The writes replaced unrelated accessibility options. Presets also compared the full string.

Both shortcuts use HOTKEYACTIVE bit `0x4`:
[Sticky Keys](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-stickykeys)
and [Filter Keys](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-filterkeys).
The readers now inspect that bit. Staging reads current flags again and preserves every other bit.
Preset literals still identify the requested direction; they are not replacement accessibility profiles.
The conflict resolver compares the resolved write with the pending primary write.

| Check | Evidence |
|---|---|
| Alternate valid states | All 1,024 low-bit combinations for each shortcut, both toggle directions. |
| Full-width flags | Unsigned 32-bit maximum preserves high bits. |
| Exact capture | Leading zeros remain in BeforeValue and rollback restores them. |
| Refuse unknown state | Missing, empty, malformed, negative, overflow, and wrong-type values cannot stage. |
| Preset detection | Shortcut intent matches customized flags without requiring a complete recipe match. |
| Preset pending state | Sticky 511 resolves to 507, which matches a staged suppression from preset literal 506. |
| Stale unrelated options | A new current flag value makes an older resolved write a conflict. |
| Rollback | A later failing change restores the exact original keyboard string through the pending-change pipeline. |
| Card behavior | Real mouse staging reads changed options; discard restores the switch. Failed reversal preserves the queued state. |
| Unavailable card | Disabled switch and reason remain visible in both themes and compact modes. |
| Host comparison | Read-only production readers recognize Sticky Keys 26 as suppressed and Filter Keys 126 as enabled. |

The app still writes saved registry options and reports a sign-out requirement.
All 2,762 CI-safe tests pass. Rendered cards were inspected in both themes and compact modes.
Annoyances, Windows Update, and Privacy keep measured content edges of 25px left, 23px right, and 59px top below edge tabs.
These checks do not test keyboard behavior after sign-out or apply changes to the host.
No policy support across Windows editions is established by this batch.

## Remaining audit

Prioritize existing controls before adding more settings:

- Verify missing-value and read-error handling in the other single-value readers. Undo must not invent a default value.
- Compare policy overrides with ordinary preferences for suggestions, Search, Widgets, diagnostics, and AI controls.
- Check partial multi-value groups, both scopes, and failure rollback.
- Check staged changes against fresh state so unrelated options are preserved.
- Verify supported edition/build behavior separately from registry round trips.

Use simulated registry values and failure injection for writes. Use the host only for read-only observations unless live changes are explicitly needed.
Automated tests establish the app's behavior over those inputs. They do not prove that Windows honors every setting.
