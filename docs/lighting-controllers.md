# Lighting engine

The Lighting tab drives every RGB device OpenRGB supports without OpenRGB being
installed. The engine is OpenRGB's device core, built from the pinned source in
`third-party/OpenRGB` (GPL-2.0-or-later, taken under this repo's GPLv3;
[why-gplv3.md](why-gplv3.md)) without its Qt GUI, plugin loader, or suspend
hook, and shipped beside the app as `lighting-engine\ThisIsMyPC-LightingEngine.exe`.
The app runs it as a hidden child process and talks to it over the OpenRGB SDK
protocol on loopback. Nothing is downloaded, nothing else is installed, and no
OpenRGB process is launched.

## Layout

| Path | What lives there |
| --- | --- |
| `third-party/OpenRGB/` | Git submodule pinned to the OpenRGB commit the engine is built from. Never edited; a bump moves the pin. |
| `src/ThisIsMyPC.LightingEngine/` | The C++ project. `ThisIsMyPC.LightingEngine.vcxproj` globs OpenRGB's `Controllers/**` and core sources from the submodule and excludes `qt/`, `PluginManager`, `SuspendResume`, `startup/`, and the Linux, macOS, and FreeBSD files, exactly as `OpenRGB.pro` does on Windows. `main.cpp` is the headless entry point. `engine_version.h` pins the version macros OpenRGB derives from git. `engine.rc` is the version resource. |
| `src/ThisIsMyPC.Core/Hardware/Lighting/` | The device model, `ILightingSession` and `ILightingBackend`, the SDK wire codec, `ILightingEngine`, and `EngineLightingBackend`, which starts the engine and lists and drives devices through the SDK. |
| `src/ThisIsMyPC.Interop.Win32/Hardware/LightingEngineHost.cs` | Runs the engine: loopback port chosen per start, configuration under the user's data folder, no window, redirected stdio, and a kill-on-close job so the engine never outlives the app. `OpenRgbSdkClient.cs` beside it is the SDK client. |
| `src/ThisIsMyPC.Lighting/` | The two C# controller ports (Sinowealth mice, ENE on NVIDIA GPU I2C) and `NativeLightingBackend`. A build without the engine falls back to them. |

## Host protocol

The engine takes OpenRGB's own options; the host passes `--config <dir>`,
`--server-port <n>`, and `--loglevel <n>`. It always starts the SDK server on
127.0.0.1 and never auto-connects to another OpenRGB.

| Direction | Line | Meaning |
| --- | --- | --- |
| engine to host, stdout | `ready <port>` | Devices detected and the server listens. |
| engine to host, stdout | `error <text>` | The server did not come up; the engine exits. |
| host to engine, stdin | `auth <64 hex characters>` | Set a new private SDK token before detection. |
| host to engine, stdin | `rescan` | Detect again; the engine answers `detected` when the pass ends. |
| host to engine, stdin | `stop` or end of input | Clean shutdown. |

The host sends a 256-bit token through stdin. A client must send its 64 hex characters
first and receive an acknowledgement before OpenRGB sees the socket. The token
never enters the command line or log. `tools/patch-lighting-network-server.ps1`
checks the pinned OpenRGB source hash and generates the guarded server under
`artifacts/`; an upstream source change stops the native build.

The engine's own log goes to `lighting-engine\logs\` under the user's data
folder, with OpenRGB's `OpenRGB.json` beside it.

## Build

The engine is not in the .NET solution; `dotnet build` never touches it. Build
it with MSBuild from any Visual Studio 2026 developer prompt:

```
msbuild src\ThisIsMyPC.LightingEngine\ThisIsMyPC.LightingEngine.vcxproj /p:Configuration=Release /p:Platform=x64
```

Output lands in `artifacts/lighting-engine/Release/`: the engine, the prebuilt
`hidapi.dll`, `libusb-1.0.dll`, and `PawnIOLib.dll` from OpenRGB's tree, the
PawnIO SMBus modules (`Smbus*.bin`, `LpcIO.bin`), and the four VC++ runtime
DLLs the prebuilt mbedtls forces (`vcruntime140*.dll`, `msvcp140*.dll`). A Debug
app run finds the engine there; a release finds it in `lighting-engine\` beside
`ThisIsMyPC.App.exe`. `tools/build-release.ps1` builds it with the pinned MSVC,
copies it into staging, and gates it through the same hardening check
(`/guard:cf`, CET, `/Brepro`) as the other first-party executables. CI builds
it with the runner's toolset; the release toolchain uses the pinned one.

## Controllers pending upstream

`src/ThisIsMyPC.LightingEngine/controllers/` holds controllers we wrote and
submitted to OpenRGB, compiled into the engine until the pin includes the
merge. Its README lists each folder with its merge request and head commit.
Today: the Royal Kludge R98 Pro (OpenRGB !3568). Keep the copies identical to
the merge request; fix upstream first, then refresh the copy.

## Update the pin

1. Move the submodule: `git -C third-party/OpenRGB fetch --depth 1 origin <commit>` and `git -C third-party/OpenRGB checkout <commit>`.
2. Set the commit, date, and version text in `src/ThisIsMyPC.LightingEngine/engine_version.h`.
3. Drop any folder under `src/ThisIsMyPC.LightingEngine/controllers/` whose merge request the new pin includes; a duplicate detector registration at link time is the sign.
4. Build. A new file OpenRGB compiles only on Windows, or one it textually includes elsewhere (`SinowealthControllerDetect.cpp` includes `GenesisXenon200Controller.cpp`), shows up as a link error; adjust the `Remove` list in the project.
5. Run `LightingEngineHostTests` (Diagnostic): it starts the engine, lists devices over the SDK, rescans, and stops it.

## What needs elevation

The app hosts the engine unelevated, so it reaches every USB, HID, and GPU
device: motherboards with Aura USB, keyboards, mice, GPUs over NVIDIA and AMD
I2C, network lights. Devices on the chipset SMBus (RGB memory, some
motherboards) need the PawnIO driver and administrator rights; the engine
carries the modules, and an elevated host under the Owner Mode service plus
a bundled PawnIO installer are the open items in the backlog.

A separately running OpenRGB owns whatever devices it opened first. The
Hardware facts already record that it is running; the tab says so.

## Rules

- Controllers write nothing the person did not ask for. Detection is reads only.
- Every write goes through the tab's live decision (`WriteDevices`); the engine never sees the override.
- Each device has a settings gear. Save to device defaults on when a separate save command is supported. The preference uses the device identity, not its enumeration index, and persists in user settings. Serial identifies a device when present; otherwise its connection location does.
- Controls edit a draft. Apply writes the mode and LED colors, then saves once when enabled and supported. Slider movements, loading, and opening settings write nothing.
- Apply reports a failed save separately from an applied change. Every device operation checks the live permission gate.
- A misbehaving device fails its own detection inside the engine; it never hides the others.
- No Qt, no OpenRGB plugins, no second copy of OpenRGB, and never OpenRGB as an installed dependency.
