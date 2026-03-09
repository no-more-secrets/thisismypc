using System.Runtime.InteropServices;

// NFR30: Restrict DLL search to System32 to prevent DLL search-order hijacking.
// CsWin32-generated P/Invoke calls (FindWindow, PostMessage, GetWindowThreadProcessId)
// load user32.dll — this ensures it's loaded from System32 only.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
