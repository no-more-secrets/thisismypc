using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>Reads the installed physical memory reported by Windows.</summary>
public sealed partial class InstalledMemoryProvider : IInstalledMemoryProvider
{
    /// <inheritdoc />
    public ulong? GetInstalledMemoryBytes()
    {
        if (!GetPhysicallyInstalledSystemMemory(out var kilobytes) || kilobytes == 0)
            return null;

        return kilobytes * 1024;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
