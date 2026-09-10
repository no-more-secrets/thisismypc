using System.Runtime.InteropServices;
using System.Text;

namespace ThisIsMyPC.Interop.Win32.Hardware.I2c;

/// <summary>
/// The handful of NvAPI entry points the GPU I2C bus needs, resolved through
/// nvapi64.dll's nvapi_QueryInterface by the interface ids OpenRGB's
/// dependencies/NVFC/nvapi.cpp uses. The library is NVIDIA's, loaded from
/// System32 only; a process that forbids non-Microsoft images reports it as
/// unavailable and Lighting carries on without GPU I2C.
/// </summary>
internal sealed unsafe class NvApi : IDisposable
{
    private const string LibraryName = "nvapi64.dll";
    private const uint IdInitialize = 0x0150E828;
    private const uint IdEnumPhysicalGpus = 0xE5AC921F;
    private const uint IdGpuGetFullName = 0xCEEE8E9F;
    private const uint IdGpuGetPciIdentifiers = 0x2DDFB66E;
    private const uint IdI2cWriteEx = 0x283AC65A;
    private const uint IdI2cReadEx = 0x4D7B0709;
    internal const int MaxPhysicalGpus = 64;
    internal const int ShortStringLength = 64;

    private readonly nint _library;
    private readonly delegate* unmanaged[Cdecl]<int> _initialize;
    private readonly delegate* unmanaged[Cdecl]<nint*, int*, int> _enumPhysicalGpus;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int> _getFullName;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, uint*, uint*, uint*, int> _getPciIdentifiers;
    private readonly delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int> _i2cWriteEx;
    private readonly delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int> _i2cReadEx;

    /// <summary>NV_I2C_INFO_V3, 64 bytes on x64; version is sizeof | (3 &lt;&lt; 16).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NvI2cInfoV3
    {
        public uint Version;
        public uint DisplayMask;
        public byte IsDdcPort;
        public byte I2cDevAddress;
        public byte* I2cRegAddress;
        public uint RegAddrSize;
        public byte* Data;
        public uint Size;
        public uint I2cSpeed;
        public uint I2cSpeedKhz;
        public byte PortId;
        public uint IsPortIdSet;
    }

    internal static uint I2cInfoVersion => (uint)sizeof(NvI2cInfoV3) | (3u << 16);

    private NvApi(nint library, delegate* unmanaged[Cdecl]<uint, nint> query)
    {
        _library = library;
        _initialize = (delegate* unmanaged[Cdecl]<int>)query(IdInitialize);
        _enumPhysicalGpus = (delegate* unmanaged[Cdecl]<nint*, int*, int>)query(IdEnumPhysicalGpus);
        _getFullName = (delegate* unmanaged[Cdecl]<nint, byte*, int>)query(IdGpuGetFullName);
        _getPciIdentifiers = (delegate* unmanaged[Cdecl]<nint, uint*, uint*, uint*, uint*, int>)query(IdGpuGetPciIdentifiers);
        _i2cWriteEx = (delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int>)query(IdI2cWriteEx);
        _i2cReadEx = (delegate* unmanaged[Cdecl]<nint, NvI2cInfoV3*, uint*, int>)query(IdI2cReadEx);
    }

    /// <summary>Loads nvapi64.dll from System32 and initializes it. Null, with the reason, when that is not possible here.</summary>
    internal static NvApi? TryLoad(out string? reason)
    {
        var path = Path.Combine(Environment.SystemDirectory, LibraryName);
        if (!File.Exists(path))
        {
            reason = "nvapi64.dll is not installed (no NVIDIA driver).";
            return null;
        }
        if (!NativeLibrary.TryLoad(path, out var library))
        {
            reason = "nvapi64.dll could not be loaded in this process (blocked by the process's image policy, or a broken driver install).";
            return null;
        }
        if (!NativeLibrary.TryGetExport(library, "nvapi_QueryInterface", out var queryAddress))
        {
            NativeLibrary.Free(library);
            reason = "nvapi64.dll has no nvapi_QueryInterface export.";
            return null;
        }
        var api = new NvApi(library, (delegate* unmanaged[Cdecl]<uint, nint>)queryAddress);
        if (api._initialize is null || api._enumPhysicalGpus is null || api._i2cReadEx is null || api._i2cWriteEx is null)
        {
            api.Dispose();
            reason = "nvapi64.dll does not expose the I2C interfaces.";
            return null;
        }
        var status = api._initialize();
        if (status != 0)
        {
            api.Dispose();
            reason = $"NvAPI_Initialize returned {status}.";
            return null;
        }
        reason = null;
        return api;
    }

    internal nint[] EnumeratePhysicalGpus()
    {
        var handles = stackalloc nint[MaxPhysicalGpus];
        var count = 0;
        var status = _enumPhysicalGpus(handles, &count);
        if (status != 0 || count <= 0)
            return [];
        var result = new nint[Math.Min(count, MaxPhysicalGpus)];
        for (var i = 0; i < result.Length; i++)
            result[i] = handles[i];
        return result;
    }

    internal string? FullName(nint gpu)
    {
        if (_getFullName is null)
            return null;
        var buffer = stackalloc byte[ShortStringLength];
        if (_getFullName(gpu, buffer) != 0)
            return null;
        var span = new ReadOnlySpan<byte>(buffer, ShortStringLength);
        var end = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? span : span[..end]);
    }

    /// <summary>PCI ids as NvAPI packs them: device id = (device &lt;&lt; 16) | vendor, subsystem id = (subdevice &lt;&lt; 16) | subvendor.</summary>
    internal bool PciIdentifiers(nint gpu, out uint deviceId, out uint subSystemId)
    {
        deviceId = 0;
        subSystemId = 0;
        if (_getPciIdentifiers is null)
            return false;
        uint device, subsystem, revision, external;
        var status = _getPciIdentifiers(gpu, &device, &subsystem, &revision, &external);
        if (status != 0)
            return false;
        deviceId = device;
        subSystemId = subsystem;
        return true;
    }

    internal int I2cRead(nint gpu, ref NvI2cInfoV3 info)
    {
        uint unknown = 0;
        fixed (NvI2cInfoV3* p = &info)
            return _i2cReadEx(gpu, p, &unknown);
    }

    internal int I2cWrite(nint gpu, ref NvI2cInfoV3 info)
    {
        uint unknown = 0;
        fixed (NvI2cInfoV3* p = &info)
            return _i2cWriteEx(gpu, p, &unknown);
    }

    public void Dispose()
    {
        if (_library != 0)
            NativeLibrary.Free(_library);
    }
}
