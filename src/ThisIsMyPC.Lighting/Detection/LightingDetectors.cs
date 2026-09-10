using ThisIsMyPC.Lighting.Controllers;
using ThisIsMyPC.Lighting.Controllers.Ene;
using ThisIsMyPC.Lighting.Controllers.Sinowealth;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Detection;

/// <summary>
/// Runs against one enumerated HID collection that matched the detector's
/// ids. <paramref name="remaining"/> is the enumeration from that collection
/// to the end, for detectors that gather several collections of one device.
/// Returns the controller, or null when the device did not answer.
/// </summary>
public delegate ILightingController? HidDetect(
    IHidTransport transport, HidDeviceInfo info, IReadOnlyList<HidDeviceInfo> remaining, string name, List<string> notes);

/// <summary>
/// One REGISTER_HID_DETECTOR line from OpenRGB. Vendor and product ids are
/// required; interface, usage page and usage are matched only when set,
/// the same as HID_INTERFACE_ANY / HID_USAGE_PAGE_ANY / HID_USAGE_ANY.
/// </summary>
public sealed record HidDetector(
    string Name,
    ushort VendorId,
    ushort ProductId,
    HidDetect Detect,
    int? Interface = null,
    ushort? UsagePage = null,
    ushort? Usage = null)
{
    public bool Matches(HidDeviceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.VendorId == VendorId
            && info.ProductId == ProductId
            && (Interface is null || Interface == info.InterfaceNumber)
            && (UsagePage is null || UsagePage == info.UsagePage)
            && (Usage is null || Usage == info.Usage);
    }
}

/// <summary>Runs against one bus whose host PCI ids matched, at the detector's address.</summary>
public delegate ILightingController? I2cDetect(II2cBus bus, byte address, string name, List<string> notes);

/// <summary>One REGISTER_I2C_PCI_DETECTOR line from OpenRGB.</summary>
public sealed record I2cPciDetector(
    string Name,
    ushort PciVendor,
    ushort PciDevice,
    ushort PciSubsystemVendor,
    ushort PciSubsystemDevice,
    byte Address,
    I2cDetect Detect)
{
    public bool Matches(I2cBusInfo bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.PciVendor == PciVendor
            && bus.PciDevice == PciDevice
            && bus.PciSubsystemVendor == PciSubsystemVendor
            && bus.PciSubsystemDevice == PciSubsystemDevice;
    }
}

/// <summary>
/// The registry of every ported detector. Adding a device means adding its
/// line here (and its controller if it is a new family); the backend and
/// the policy learn about it from this list alone.
/// </summary>
public static class LightingDetectors
{
    public static IReadOnlyList<HidDetector> Hid { get; } =
    [
        .. SinowealthDetectors.Hid,
    ];

    public static IReadOnlyList<I2cPciDetector> I2cPci { get; } =
    [
        .. EneGpuDetectors.I2cPci,
    ];
}
