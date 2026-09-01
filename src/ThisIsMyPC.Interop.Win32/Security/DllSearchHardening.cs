using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// Process-wide DLL search hardening, called first thing in every executable's
/// entry point. SetDefaultDllDirectories removes the working directory and PATH
/// from every LoadLibrary resolution in the process, including delay-loaded and
/// dependency-pulled DLLs that per-P/Invoke DefaultDllImportSearchPaths
/// attributes cannot reach. The application directory stays allowed (Skia,
/// ANGLE, and SQLite natives ship next to the exe; at release that directory
/// is admin-only Program Files).
/// </summary>
public static partial class DllSearchHardening
{
    private const uint LOAD_LIBRARY_SEARCH_APPLICATION_DIR = 0x00000200;
    private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    /// <summary>Best-effort: a failure must never stop startup, only lose hardening.</summary>
    public static bool Apply() =>
        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint directoryFlags);
}
