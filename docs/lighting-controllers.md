# Built-in lighting controllers

The Lighting tab drives RGB devices itself. The device protocols are ports of
OpenRGB's controllers (GPL-2.0-or-later, taken under this repo's GPLv3;
[why-gplv3.md](why-gplv3.md)). No OpenRGB binary ships with the app and none is
launched. This page says where the code lives, how a device gets found, and
how to port the next controller family.

## Layout

| Project | What lives there |
| --- | --- |
| `src/ThisIsMyPC.Core/Hardware/Lighting/` | The device model (`LightingDevice`, `LightingMode`, `LightingZone`, `RgbColor`), the `ILightingSession` and `ILightingBackend` contracts, and the OpenRGB SDK wire codec kept for diagnostics. |
| `src/ThisIsMyPC.Lighting/` | Pure: transport interfaces, the detector registry, the ported controllers, and `NativeLightingBackend`. No Win32 calls; every device access goes through `IHidTransport` or `II2cBus`, so the controllers run against fakes in `tests/ThisIsMyPC.Lighting.Tests`. |
| `src/ThisIsMyPC.Interop.Win32/Hardware/Hid/` | `WindowsHidTransport`: setupapi.dll and hid.dll with hidapi's Windows behavior (one entry per top-level collection, zero-access enumeration handles, shared read/write opens). |
| `src/ThisIsMyPC.Interop.Win32/Hardware/I2c/` | `NvApiI2cBusProvider`: one I2C bus per NVIDIA GPU over `NvAPI_I2CReadEx`/`WriteEx`, SMBus transactions shaped as OpenRGB's `i2c_smbus_nvapi.cpp` shapes them. |
| `src/ThisIsMyPC.App/Services/VendorNativeDependencyLoader.cs` | Maps `nvapi64.dll` from System32 before Code Integrity Guard closes image loading, after checking its NVIDIA signature. Optional and never fatal. |

Detection order inside `NativeLightingBackend.Detect`: enumerate HID collections, run every `HidDetector` whose vendor, product, interface, usage page and usage match (unset fields match anything, the same as OpenRGB's `HID_*_ANY`); then enumerate I2C buses and run every `I2cPciDetector` whose PCI vendor, device, subsystem vendor and subsystem device match the bus's host, at the detector's address. One pass at a time, cached until a refresh; the page's sessions share the controllers the pass found, and every controller call runs off the UI thread under that controller's own lock.

## Ported families

| Family | OpenRGB source | Transport | Devices |
| --- | --- | --- | --- |
| Sinowealth (`Controllers/Sinowealth/`) | `Controllers/SinowealthController/` | HID feature reports on two vendor collections | Glorious Model O / O-, Model D / D-, Everest GT-100 |
| ENE SMBus (`Controllers/Ene/`) | `Controllers/ENESMBusController/` (`ENESMBusInterface_i2c_smbus`, `RGBController_ENESMBus`) | SMBus register protocol over `II2cBus` | ASUS graphics cards at 0x67 (`EneGpuDetectors.g.cs`, 172 PCI ids) |

Not ported yet: ENE on the chipset SMBus (ASUS motherboards, RGB memory) needs a
PawnIO-backed `II2cBus`, which needs administrator rights and the driver; AMD
GPUs need an ADL-backed bus; everything else in OpenRGB's catalog.

## Port another device family

1. Fetch the source. `tools/fetch-openrgb-controller.ps1 -Controller <FolderName>` downloads `Controllers/<FolderName>/` from the OpenRGB GitLab tree into `artifacts/openrgb-src/<revision>/`, plus `pci_ids.h` and the shared headers the detector files include. It prints the revision it fetched; record it in the controller's summary comment.
2. Read `RGBController_<X>.cpp` for the mode table (names, flags, ranges, color modes) and `<X>Controller.cpp` for the bytes on the wire. Port both into one `ILightingController` under `src/ThisIsMyPC.Lighting/Controllers/<Family>/`. Keep OpenRGB's mode values and register offsets verbatim; keep the color byte order they use (ENE stores R, B, G; Sinowealth writes R, B, G). Product copy in mode names may be fixed ("Seamless Breathing").
3. Port the detector. HID families: one `HidDetector` per `REGISTER_HID_DETECTOR*` line, in a static list the registry references. Detectors that gather several collections of one device get the enumeration from the matched collection onward (`remaining`) and must apply the same remainder rule OpenRGB's `DetectUsages` applies, or the device is found once per collection. I2C PCI families: run `tools/import-openrgb-pci-detectors.ps1` over the detector file to generate the id table; commit the generated file; write the detect method it names.
4. Register it in `LightingDetectors` (`Detection/LightingDetectors.cs`). The backend, the policy and the evidence list learn about the family from that list alone.
5. Test it against a fake. `tests/ThisIsMyPC.Lighting.Tests/Fakes/` has a scripted HID transport and a simulated ENE chip; add what the family needs. Assert the exact bytes for at least one mode write, the detection remainder rule where it applies, and a refused transport call.
6. Try it on real hardware with `NativeLightingLiveTests` (Diagnostic; `Detect_ListsThisPcsDevices` reads only, `PerLedColor_WritesAndRestores` writes when `TIPC_LIGHTING_WRITE=1`). A vendor library the family needs must be Microsoft-signed, or added to `VendorNativeDependencyLoader.Optional` with its signer, or the release build cannot load it.

Reading OpenRGB's `RGBController.cpp` is the fastest way to learn the model: `SetupZones`, `DeviceUpdateMode`, `DeviceUpdateLEDs` map one to one onto `Describe`, `SetMode`, `SetLeds` here.

## Rules

- Controllers write nothing the person did not ask for. Detection is reads only (the ENE probe reads registers; the Sinowealth probe sends the configuration-read command).
- Every write goes through the tab's live decision (`WriteDevices`); the controllers never see the override.
- Each device has a settings gear. Save to device defaults on when a separate save command is supported. The preference uses the device identity, not its enumeration index, and persists in user settings. Serial identifies a device when present; otherwise its connection location does. Moving a device without a serial may reset its preference.
- Controls edit a draft. Apply writes the mode and LED colors, then saves once when enabled and supported. Slider movements, loading, and opening settings write nothing. Automatic-save modes receive no extra save command and devices with only those modes have no save toggle. Unsupported modes never receive a save command.
- Apply reports a failed save separately from an applied change. Every device operation checks the live permission gate; closing the card prevents later operations in an unfinished apply.
- Sam verified the existing ENE save command on his ASUS ROG STRIX RTX 4080: lighting stayed Off through shutdown and startup. This is evidence for that card, not every ENE device.
- A misbehaving device fails its own detection with a note; it never hides the others.
