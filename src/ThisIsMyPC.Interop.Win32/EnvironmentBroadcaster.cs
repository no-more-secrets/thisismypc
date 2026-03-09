using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32;

public sealed partial class EnvironmentBroadcaster : IEnvironmentBroadcaster
{
    private const nint HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint TimeoutMs = 5000;

    public void BroadcastEnvironmentChange()
    {
        SendMessageTimeoutW(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            0,
            "Environment",
            SMTO_ABORTIFHUNG,
            TimeoutMs,
            out _);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessageTimeoutW(
        nint hWnd,
        uint msg,
        nuint wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out nuint lpdwResult);
}
