using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>Read-only DXGI adapter memory and Windows-volume disk identity. Opens no handle with write access.</summary>
public static partial class HomeHardwareReader
{
    public static unsafe IReadOnlyList<GraphicsIdentity> ReadGraphics()
    {
        var result = new List<GraphicsIdentity>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(in iid, out var factory) < 0 || factory == 0) return result;
        try
        {
            var table = *(nint**)factory;
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)table[12];
            for (uint index = 0; index < 64; index++)
            {
                nint adapter = 0;
                if (enumerate(factory, index, &adapter) < 0 || adapter == 0) break;
                try
                {
                    var desc = new AdapterDescription();
                    var getDesc = (delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)(*(nint**)adapter)[10];
                    if (getDesc(adapter, &desc) >= 0)
                        result.Add(new(new string(desc.Description, 0, 128).TrimEnd('\0'), (ulong)desc.DedicatedVideoMemory, (desc.Flags & 2) != 0));
                }
                finally { Release(adapter); }
            }
        }
        finally { Release(factory); }
        return result;
    }

    public static IReadOnlyList<StorageIdentity> ReadStorage()
    {
        var result = new List<StorageIdentity>();
        int? bootNumber = null;
        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (root is not null)
        {
            using var volume = Open(@"\\.\" + root.TrimEnd('\\'));
            var number = new byte[12];
            if (!volume.IsInvalid && DeviceIoControl(volume, 0x2D1080, null, 0, number, 12, out var read, 0) && read >= 12
                && BinaryPrimitives.ReadUInt32LittleEndian(number) == 7)
                bootNumber = BinaryPrimitives.ReadInt32LittleEndian(number.AsSpan(4));
        }
        // Bounded discovery includes disks without drive letters. Missing device numbers are normal.
        for (var index = 0; index < 256; index++)
        {
            using var disk = Open(@"\\.\PhysicalDrive" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (disk.IsInvalid) continue;
            var query = new byte[12]; // StorageDeviceProperty, PropertyStandardQuery
            var descriptor = new byte[65536];
            if (!DeviceIoControl(disk, 0x2D1400, query, (uint)query.Length, descriptor, (uint)descriptor.Length, out var read, 0)
                || read < 36) continue;
            var name = ReadStorageName(descriptor.AsSpan(0, (int)Math.Min(read, (uint)descriptor.Length)));
            result.Add(new(name ?? $"Disk {index}", index, bootNumber is { } boot ? index == boot : null));
        }
        return result;
    }

    public static string? ReadStorageName(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 36) return null;
        var vendor = ReadText(descriptor, BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..]));
        var product = ReadText(descriptor, BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..]));
        if (string.IsNullOrWhiteSpace(product)) return vendor;
        return string.IsNullOrWhiteSpace(vendor) || product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
            ? product : vendor + " " + product;
    }

    private static string? ReadText(ReadOnlySpan<byte> buffer, uint offset)
    {
        if (offset < 36 || offset >= buffer.Length) return null;
        var text = buffer[(int)offset..];
        var end = text.IndexOf((byte)0);
        return end < 0 ? null : Encoding.ASCII.GetString(text[..end]).Trim();
    }

    private static SafeFileHandle Open(string path) => CreateFileW(path, 0, 3, 0, 3, 0, 0);
    private static unsafe void Release(nint value) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)value)[2])(value);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CreateDXGIFactory1(in Guid iid, out nint factory);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, uint inputLength,
        [Out] byte[] output, uint outputLength, out uint returned, nint overlapped);
}
