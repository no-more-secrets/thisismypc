using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Display;

/// <summary>Enumerates display adapters that Windows currently attaches to the desktop.</summary>
public sealed class GpuIdentityProvider : IGpuIdentityProvider
{
    private const uint AttachedToDesktop = 0x00000001;
    private const uint MirroringDriver = 0x00000008;

    /// <inheritdoc />
    public IReadOnlyList<string> GetCurrentAdapterNames()
    {
        var adapters = new List<string>();
        for (uint index = 0; ; index++)
        {
            var device = new NativeDisplay.DISPLAY_DEVICEW
            {
                cb = (uint)Marshal.SizeOf<NativeDisplay.DISPLAY_DEVICEW>(),
            };
            if (NativeDisplay.EnumDisplayDevicesW(null, index, ref device, 0) == 0)
                break;

            var attached = (device.StateFlags & AttachedToDesktop) != 0;
            var mirroring = (device.StateFlags & MirroringDriver) != 0;
            if (attached && !mirroring && !string.IsNullOrWhiteSpace(device.DeviceString))
                adapters.Add(device.DeviceString.Trim());
        }

        return adapters.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
