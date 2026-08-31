using Windows.Win32;

namespace ThisIsMyPC.Interop.Win32;

/// <summary>
/// Allocates a separate console window so Debug builds can stream verbose logs
/// live. Call before anything touches System.Console: the runtime caches its
/// std handles on first use, so a late allocation would write into the void.
/// </summary>
public static class DebugConsole
{
    public static bool Attach() => PInvoke.AllocConsole();
}
