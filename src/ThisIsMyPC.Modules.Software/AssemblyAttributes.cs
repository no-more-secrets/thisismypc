using System.Runtime.InteropServices;

// NFR30: Restrict DLL search to System32 to prevent DLL search-order hijacking.
// Preventive — ensures any future P/Invoke in this assembly is covered.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
