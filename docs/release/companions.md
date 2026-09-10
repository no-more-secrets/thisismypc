# Bundled companions

ThisIsMyPC ships third-party programs it drives over a documented interface. They are not linked into the app. They run as separate processes and are reached over their own protocol. Nothing here is a shell-out to an opaque tool. The interface is the program's public SDK, and the app never parses its window or its files.

## OpenRGB (Lighting)

| Item | Value |
| --- | --- |
| Pinned release | `tools/companion-manifest.json` (`openRgb.version`, archive URL, SHA-256) |
| License | GPL-2.0-or-later. The app is GPLv3, so shipping the unmodified binaries is permitted; the NOTICE names the version and the source tag. |
| Installed to | `companions\OpenRGB\` next to `ThisIsMyPC.App.exe` |
| Files | The subset `bundledFiles` lists: the executable, the USB and HID libraries, the PawnIO modules, the three Qt libraries and the Windows platform plugin. Image, style and OpenGL plugins are not needed for a headless server. |
| Runs as | `OpenRGB.exe --server --server-host 127.0.0.1 --server-port 6742 --config %LocalAppData%\ThisIsMyPC\openrgb --noautoconnect --loglevel 3`, no window |
| Lifetime | Started when the Lighting page opens and no SDK server answers on the port. Placed in a kill-on-close job object, so it dies with the app. Stopped explicitly when the app exits. |
| Reached over | The OpenRGB SDK protocol on localhost (`Core/Hardware/Lighting/LightingWire.cs`, protocol version 4) |

A user's own OpenRGB that already answers on the SDK port is used as is; a second server would fight it for the devices. The bundled server keeps its own configuration folder, so it never edits the user's OpenRGB settings.

### Fetch and verify

```
.\tools\get-openrgb-archive.ps1
```

Downloads the pinned archive into `artifacts/tool-cache/openrgb/<version>/`, verifies the SHA-256 from the manifest, and extracts it beside the archive. Inside a source checkout the app runs that extracted copy, so development builds and the diagnostic tests drive the same bundled build a release ships. `build-release.ps1` calls `copy-bundled-companions.ps1`, which copies the listed files into the staging tree and writes `NOTICE.txt`.

### Release gates

The companion files are third-party. They are not signed with the No More Secrets certificate, and the signing wrapper's exclusion pattern skips the `companions` folder. Some of them (the Qt libraries) carry the Qt Company's own signature. They are not run through the first-party PE hardening checks. They are byte-identical to the verified upstream archive, and the reproducible-build comparison hashes that folder without asking for our signature. A fresh clone needs network access to Codeberg once to fill the tool cache. Updating the pin means: change the manifest (version, URL, hash, file list), run the fetch script, run the `OpenRgbLiveTests` diagnostic, and record the change in the backlog.

### Known limits

- SMBus devices (motherboard headers, RAM) need the PawnIO driver, which OpenRGB expects to be installed separately with administrator rights. Without it the server still starts and finds USB and GPU devices. Bundling or installing PawnIO is a separate owner decision.
- The server runs with the app's token (unelevated). Devices that need elevation stay invisible until that is designed.
- Protocol version 4 is negotiated even with a newer server, so per-zone modes and device configuration strings (version 6) are not read.
