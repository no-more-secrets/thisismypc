# ExplorerPatcher settings integration

The catalog and native mappings follow ExplorerPatcher `26100.8457.70.3`.
The reference implementation is its [GUI](https://github.com/valinet/ExplorerPatcher/blob/26100.8457.70.3/ep_gui/GUI.c),
[settings manifest](https://github.com/valinet/ExplorerPatcher/blob/26100.8457.70.3/ep_gui/resources/settings.reg),
and [registration code](https://github.com/valinet/ExplorerPatcher/blob/26100.8457.70.3/ExplorerPatcher/dllmain.c).

Registration writes the installed native and IA-32 DLL paths, their apartment
threading model, and the DriveMask value. It does not run regsvr32 or load a DLL
inside ThisIsMyPC. The pending group captures each value, its type, and leaf-key
absence. Unknown registrations are rejected. Undo removes newly created empty
leaf keys and preserves pre-existing keys and unrelated values.

Navigation-bar suppression uses CLSID `056440FD-8568-48e7-A632-72157243B55B`.
It is separate from the classic command-bar override. Registration-dependent
controls remain visible but disabled until registration is applied and refreshed.
Other applications must reopen their dialogs to load the extension.

Square window corners use ExplorerPatcher's installed `ep_dwm_svc.exe` with its
fixed service name and arguments. Before-state distinguishes an absent service
from its start type and running state. Turning the option off stops and disables
an existing service without losing its configuration. Undo removes a service
created by the change. Failed operations attempt to restore the previous state
and report restoration failures.

Taskbar auto-hide uses `SHAppBarMessage` with `ABM_GETSTATE` and `ABM_SETSTATE`.
The change modifies `ABS_AUTOHIDE` while preserving the other appbar flag.

Four virtual Start values write both ExplorerPatcher registry locations, as its
GUI does. Each location keeps its own before-state. The two Start policy choices
write their documented Windows policy locations. Start controls that need missing
Windows 10 files remain visible with an explanation.

All writes use the pending-change pipeline and broker allowlist. Tests use fake
registry and service implementations. Screenshot scans are read-only. Live
registration, service changes, taskbar changes, and their visual effects still
require Windows acceptance testing.
