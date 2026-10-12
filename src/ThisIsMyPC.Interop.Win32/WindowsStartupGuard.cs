using System.Runtime.InteropServices;
using ThisIsMyPC.Core;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>Rejects unsupported Windows versions before executable startup.</summary>
public static partial class WindowsStartupGuard
{
    /// <summary>Checks Windows and shows the minimum requirement when unsupported.</summary>
    /// <returns>True when startup can continue.</returns>
    public static bool Check()
    {
        if (OperatingSystem.IsWindows() && WindowsRequirements.IsSupported(Environment.OSVersion.Version))
            return true;

        if (OperatingSystem.IsWindows())
            _ = MessageBox(nint.Zero, WindowsRequirements.UnsupportedVersionMessage, "ThisIsMyPC", 0x10);
        return false;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint window, string text, string caption, uint type);
}
