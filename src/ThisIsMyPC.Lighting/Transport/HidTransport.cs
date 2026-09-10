namespace ThisIsMyPC.Lighting.Transport;

/// <summary>
/// One HID top-level collection as Windows exposes it: one device path per
/// collection, so a mouse with three vendor collections appears three times
/// with the same vendor and product ids. Mirrors hidapi's hid_device_info,
/// which is what OpenRGB's detectors match against.
/// </summary>
/// <param name="InterfaceNumber">USB interface number (the "&amp;mi_XX" part of the path), or -1 when the path has none.</param>
public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    int InterfaceNumber,
    ushort UsagePage,
    ushort Usage,
    string? Manufacturer,
    string? Product,
    string? SerialNumber,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength);

/// <summary>Enumerates and opens HID collections. Interop.Win32 implements it over hid.dll; tests script it.</summary>
public interface IHidTransport
{
    /// <summary>Every present HID collection, in enumeration order. Never throws; an unreadable collection is skipped.</summary>
    IReadOnlyList<HidDeviceInfo> Enumerate();

    /// <summary>Opens a collection for feature reports and I/O. Null when Windows refuses (exclusive device, access denied).</summary>
    IHidDevice? Open(string path);
}

/// <summary>
/// An open HID collection with hidapi's calling conventions: the first byte
/// of every buffer is the report id, and every call returns the byte count
/// on success or -1 on failure. No call throws.
/// </summary>
public interface IHidDevice : IDisposable
{
    HidDeviceInfo Info { get; }

    int SendFeatureReport(ReadOnlySpan<byte> report);

    /// <summary>Fills <paramref name="buffer"/>, whose first byte names the report to fetch.</summary>
    int GetFeatureReport(Span<byte> buffer);

    int Write(ReadOnlySpan<byte> report);

    /// <summary>Reads one input report, or returns 0 when nothing arrived within <paramref name="timeout"/>.</summary>
    int Read(Span<byte> buffer, TimeSpan timeout);
}
