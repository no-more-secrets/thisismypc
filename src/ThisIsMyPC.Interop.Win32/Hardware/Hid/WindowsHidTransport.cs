using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Interop.Win32.Hardware.Hid;

/// <summary>
/// HID over setupapi.dll and hid.dll, with hidapi's Windows behavior so the
/// ported detectors see what OpenRGB sees: one entry per top-level
/// collection, attributes and capabilities read through a zero-access
/// handle (so keyboards and mice enumerate too), the interface number
/// parsed from the path, and opens with read/write access shared with
/// other programs. Both DLLs are Microsoft-signed, so this runs inside the
/// guarded app process.
/// </summary>
public sealed class WindowsHidTransport : IHidTransport
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.Hid");

    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        var list = new List<HidDeviceInfo>();
        NativeHid.HidD_GetHidGuid(out var hidGuid);
        var set = NativeHid.SetupDiGetClassDevsW(in hidGuid, null, 0, NativeHid.DIGCF_PRESENT | NativeHid.DIGCF_DEVICEINTERFACE);
        if (set == 0 || set == -1)
        {
            Log.Warn("SetupDiGetClassDevs failed (Win32 {Code})", Marshal.GetLastPInvokeError());
            return list;
        }
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new NativeHid.SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<NativeHid.SP_DEVICE_INTERFACE_DATA>() };
                if (!NativeHid.SetupDiEnumDeviceInterfaces(set, 0, in hidGuid, index, ref data))
                    break;
                var path = InterfacePath(set, ref data);
                if (path is null)
                    continue;
                var info = Describe(path);
                if (info is not null)
                    list.Add(info);
            }
        }
        finally
        {
            NativeHid.SetupDiDestroyDeviceInfoList(set);
        }
        return list;
    }

    private static string? InterfacePath(nint set, ref NativeHid.SP_DEVICE_INTERFACE_DATA data)
    {
        NativeHid.SetupDiGetDeviceInterfaceDetailW(set, ref data, 0, 0, out var required, 0);
        if (required == 0 || Marshal.GetLastPInvokeError() != NativeHid.ERROR_INSUFFICIENT_BUFFER)
            return null;
        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize (8 on x64 with packing) then the path.
            Marshal.WriteInt32(buffer, 8);
            if (!NativeHid.SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, required, out _, 0))
                return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Reads ids, usage and strings through a zero-access handle, hidapi's enumeration open.</summary>
    private static HidDeviceInfo? Describe(string path)
    {
        using var handle = NativeHid.CreateFileW(path, 0, NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE, 0, NativeHid.OPEN_EXISTING, NativeHid.FILE_FLAG_OVERLAPPED, 0);
        if (handle.IsInvalid)
            return null;

        var attributes = new NativeHid.HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<NativeHid.HIDD_ATTRIBUTES>() };
        if (!NativeHid.HidD_GetAttributes(handle, ref attributes))
            return null;

        ushort usagePage = 0, usage = 0, input = 0, output = 0, feature = 0;
        if (NativeHid.HidD_GetPreparsedData(handle, out var preparsed) && preparsed != 0)
        {
            try
            {
                if (NativeHid.HidP_GetCaps(preparsed, out var caps) == HidpStatusSuccess)
                {
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    input = caps.InputReportByteLength;
                    output = caps.OutputReportByteLength;
                    feature = caps.FeatureReportByteLength;
                }
            }
            finally
            {
                NativeHid.HidD_FreePreparsedData(preparsed);
            }
        }

        return new HidDeviceInfo(
            path,
            attributes.VendorID,
            attributes.ProductID,
            InterfaceNumber(path),
            usagePage,
            usage,
            ReadString(handle, NativeHid.HidD_GetManufacturerString),
            ReadString(handle, NativeHid.HidD_GetProductString),
            ReadString(handle, NativeHid.HidD_GetSerialNumberString),
            input,
            output,
            feature);
    }

    private const int HidpStatusSuccess = 0x00110000;

    private delegate bool StringReader(SafeFileHandle device, ref ushort buffer, uint bufferLength);

    private static string? ReadString(SafeFileHandle handle, StringReader reader)
    {
        Span<char> buffer = stackalloc char[256];
        buffer.Clear();
        if (!reader(handle, ref System.Runtime.CompilerServices.Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(buffer)), (uint)(buffer.Length * sizeof(char))))
            return null;
        var end = buffer.IndexOf('\0');
        var text = new string(end < 0 ? buffer : buffer[..end]);
        return text.Length == 0 ? null : text;
    }

    /// <summary>hidapi reads the USB interface number from the "&amp;mi_XX" token of the device path; -1 without one.</summary>
    internal static int InterfaceNumber(string path)
    {
        var at = path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
        if (at < 0 || at + 6 > path.Length)
            return -1;
        return int.TryParse(path.AsSpan(at + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number) ? number : -1;
    }

    public IHidDevice? Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var info = Describe(path);
        if (info is null)
            return null;
        var handle = NativeHid.CreateFileW(path, NativeHid.GENERIC_READ | NativeHid.GENERIC_WRITE,
            NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE, 0, NativeHid.OPEN_EXISTING, NativeHid.FILE_FLAG_OVERLAPPED, 0);
        if (handle.IsInvalid)
        {
            Log.Debug("HID open refused for {Path} (Win32 {Code})", path, Marshal.GetLastPInvokeError());
            handle.Dispose();
            return null;
        }
        return new Device(handle, info);
    }

    private sealed class Device(SafeFileHandle handle, HidDeviceInfo info) : IHidDevice
    {
        private readonly object _lock = new();

        public HidDeviceInfo Info => info;

        public int SendFeatureReport(ReadOnlySpan<byte> report)
        {
            lock (_lock)
                return NativeHid.HidD_SetFeature(handle, in MemoryMarshal.GetReference(report), (uint)report.Length) ? report.Length : -1;
        }

        public int GetFeatureReport(Span<byte> buffer)
        {
            lock (_lock)
                return NativeHid.HidD_GetFeature(handle, ref MemoryMarshal.GetReference(buffer), (uint)buffer.Length) ? buffer.Length : -1;
        }

        /// <summary>hid_write: the report padded to the output report length, sent through overlapped I/O. The buffer is pinned for the whole transfer: the driver keeps its address after the call returns pending.</summary>
        public int Write(ReadOnlySpan<byte> report)
        {
            var length = Math.Max(report.Length, info.OutputReportLength);
            var padded = GC.AllocateArray<byte>(length, pinned: true);
            report.CopyTo(padded);
            lock (_lock)
            {
                var overlapped = new NativeHid.OVERLAPPED { hEvent = NativeHid.CreateEventW(0, false, false, null) };
                try
                {
                    if (!NativeHid.WriteFile(handle, in padded[0], (uint)length, out var written, ref overlapped))
                    {
                        if (Marshal.GetLastPInvokeError() != NativeHid.ERROR_IO_PENDING)
                            return -1;
                        if (!NativeHid.GetOverlappedResult(handle, ref overlapped, out written, true))
                            return -1;
                    }
                    return (int)written;
                }
                finally
                {
                    NativeHid.CloseHandle(overlapped.hEvent);
                }
            }
        }

        public int Read(Span<byte> buffer, TimeSpan timeout)
        {
            var length = Math.Max(buffer.Length, info.InputReportLength);
            var scratch = GC.AllocateArray<byte>(length, pinned: true);
            lock (_lock)
            {
                var overlapped = new NativeHid.OVERLAPPED { hEvent = NativeHid.CreateEventW(0, false, false, null) };
                try
                {
                    uint read;
                    if (!NativeHid.ReadFile(handle, ref scratch[0], (uint)length, out read, ref overlapped))
                    {
                        if (Marshal.GetLastPInvokeError() != NativeHid.ERROR_IO_PENDING)
                            return -1;
                        var wait = NativeHid.WaitForSingleObject(overlapped.hEvent, (uint)Math.Clamp(timeout.TotalMilliseconds, 0, uint.MaxValue - 1));
                        if (wait != NativeHid.WAIT_OBJECT_0)
                        {
                            NativeHid.CancelIo(handle);
                            NativeHid.GetOverlappedResult(handle, ref overlapped, out _, true);
                            return 0;
                        }
                        if (!NativeHid.GetOverlappedResult(handle, ref overlapped, out read, false))
                            return -1;
                    }
                    var count = (int)Math.Min(read, (uint)buffer.Length);
                    scratch.AsSpan(0, count).CopyTo(buffer);
                    return count;
                }
                finally
                {
                    NativeHid.CloseHandle(overlapped.hEvent);
                }
            }
        }

        public void Dispose() => handle.Dispose();
    }
}
