using System.Runtime.InteropServices;

namespace ThisIsMyPC.Broker;

internal static partial class NativeConfirmation
{
    private const uint OkCancel = 0x00000001;
    private const uint IconWarning = 0x00000030;
    private const uint DefaultButtonTwo = 0x00000100;
    private const uint SetForeground = 0x00010000;
    private const int Ok = 1;

    internal static bool Confirm(string message) => MessageBoxW(
        0,
        message,
        "ThisIsMyPC administrator confirmation",
        OkCancel | IconWarning | DefaultButtonTwo | SetForeground) == Ok;

    internal static void Error(string message) => _ = MessageBoxW(
        0,
        message,
        "ThisIsMyPC privilege broker",
        0x00000010 | SetForeground);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MessageBoxW(nint window, string text, string caption, uint type);
}
