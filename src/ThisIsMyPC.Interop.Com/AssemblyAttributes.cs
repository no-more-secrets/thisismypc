using System.Runtime.InteropServices;

// NFR30: Restrict DLL search to System32 to prevent DLL search-order hijacking.
// Belt-and-suspenders with per-declaration attributes in ContextMenuProbe.cs.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
