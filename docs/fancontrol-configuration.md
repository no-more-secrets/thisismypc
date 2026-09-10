# FanControl configuration integration

Investigated on 2026-09-10 against Sam's installed FanControl 226, .NET Framework 4.8 build.
The folder name contains 199; the executable and configuration both report 226.
These observations describe that build. They are not a stable schema guarantee for later releases.

## What the configuration exposes

`Configurations/userConfig.json` contains `__VERSION__`, `Main`, and `Sensors`.

| Section | Observed fields |
| --- | --- |
| `Main.Controls` | Hardware identifier, enabled state, manual mode/value, selected curve, minimum percentage, offset, start/stop values, step limits, calibration, paired RPM sensor |
| `Main.FanCurves` | Named flat curves and graph curves, point strings, temperature source, command mode, hysteresis, response times |
| `Main.CustomSensors` | Custom sensor configuration |
| `Main` display settings | Hidden cards, orientation, theme, tray icons |
| `Sensors` | Backend enablement, disabled plugins, storage sensor behavior, vendor settings |
| Separate `Configurations/CACHE` | Last saved configuration filename, update checks, minimized startup, window placement |

Curve definitions also appear nested in each control's `SelectedFanCurve`.
An editor must reconcile those copies. Editing only the top-level curve list is not yet proven sufficient.
Preserve unknown fields, hardware identifiers, sensor pairing, calibration, and command mode when creating presets.
Startup tasks and newer service installation state are outside the main configuration.

## Live experiment

The experiment copied the existing file and changed exactly two values:

- Renamed the Chassis Fans card with a temporary test suffix.
- Changed an unused flat curve from 40 to 41 percent. No control selected that curve.

An elevated invocation of the documented `-c` command loaded this separate test profile into the existing process.
Windows UI Automation found the edited nickname in FanControl's window.
The original profile was then requested through `-c userConfig.json`.
Its file remained byte-identical to the backup. Active curve definitions and calibration were never edited.

This proves external file edits can reach the running application's configuration.
It does not prove every field reloads, or prove a changed active curve produces the intended physical fan response.
The unused curve's runtime value was not separately inspected.
After restoration, UI Automation found the original card nickname and no test nickname. The temporary installed profile was removed.

`CACHE.CurrentConfigFileName` stayed `userConfig.json` while the edited nickname was visible.
Do not use that persisted value as an acknowledgement of the currently loaded profile.

## Existing-instance behavior

FanControl 226 uses a named-pipe IPC interface to forward commands to its existing instance.
A diagnostic call from an unelevated process failed with `UnauthorizedAccessException`.
An executable launch returned exit code zero without establishing that the profile loaded.
Neither a successful launch nor an old CACHE value establishes command success.

ThisIsMyPC now checks for a running FanControl before launching it again.
It activates an accessible main window in the same session with the exact executable path.
If no accessible window exists, it tells the user to use FanControl's notification-area icon.
It does not relaunch or elevate FanControl in that case. Tray-only activation through IPC remains unresolved.

The diagnostic inspection used the installed IPC assembly to understand the boundary.
No proprietary implementation or FanControl assembly is copied into the application or this repository.
No internal IPC endpoint is adopted as a production dependency.

## Profile editor

Cooling edits local version 226 profiles from the detected FanControl installation's `Configurations` folder.
Start from a complete existing profile. Saving a copy is the default.
The editor supports fan names, enabled state, manual speed, curve assignments, flat percentages, and graph points.
Other curve types remain unchanged. Calibration, hardware identifiers, sensor pairing, and unknown properties are preserved.
Changes to a selected curve also update its nested definitions in fan controls.

Saving stages a file change for review. Apply writes the file; history can undo or redo the file change.
An unchanged document retains its original bytes. Undo restores captured bytes and refuses to overwrite subsequent edits.
Saves use the desktop account's existing file permissions. They do not extend the elevated broker's file access.
If the folder is not writable, saving reports the access failure.
Undoing a new copy compares and deletes through one exclusive native file handle, preventing concurrent saves between those steps.
New copies use a flushed temporary file and a move that refuses existing targets.
Replacing an existing profile holds exclusive access and restores bytes after an I/O failure; it is not atomic across process termination.

Saving does not load the profile or change the running fan controls.
Open FanControl and load the saved profile there. File history cannot restore an unknown runtime selection.
FanControl can save its own loaded profile, so prefer copies and reload the editor after external changes.

Remaining verification:

1. Verify active curve changes with physical observations and a tested restoration path.
2. Check other FanControl versions and its newer service mode before claiming support.
3. Add reliable profile activation independently from CACHE and process exit status.

The plugin API remains useful for sensors and controls but does not expose existing curve editing.
A plugin could provide a narrow window-activation interface through its supported application-control hook, subject to separate implementation and verification.

## Sources

- [FanControl command-line and control documentation](https://getfancontrol.com/docs/).
- [FanControl plugin API](https://github.com/Rem0o/FanControl.Releases/wiki/Plugins).
- [Maintainer discussion of external configuration switching](https://github.com/Rem0o/FanControl.Releases/discussions/3975).
- Installed version 226 configuration, plugin XML documentation, and read-only IPC metadata inspection.

Private profile copies and diagnostic results remain under gitignored `artifacts/diagnostics/fancontrol-config/`.
