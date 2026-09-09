using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// Prevents Windows from showing process-blocking critical-error dialogs.
/// CIG still rejects untrusted images; callers receive the load failure instead.
/// </summary>
public static partial class CriticalErrorDialogHardening
{
    private const uint SemFailCriticalErrors = 0x0001;

    public static bool Apply()
    {
        var current = GetErrorMode();
        _ = SetErrorMode(current | SemFailCriticalErrors);
        return IsEnabled();
    }

    public static bool IsEnabled() => (GetErrorMode() & SemFailCriticalErrors) != 0;

    [LibraryImport("kernel32.dll")]
    private static partial uint GetErrorMode();

    [LibraryImport("kernel32.dll")]
    private static partial uint SetErrorMode(uint mode);
}
