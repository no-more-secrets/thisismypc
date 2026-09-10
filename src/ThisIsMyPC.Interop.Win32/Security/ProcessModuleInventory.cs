using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>Reads loaded module paths from the current Windows process.</summary>
public static partial class ProcessModuleInventory
{
    private const int MaximumModules = 512;
    private const int MaximumPathLength = 32768;
    private static readonly nint CurrentProcess = new(-1);

    /// <summary>Returns every module path currently mapped into this process.</summary>
    /// <returns>Absolute module paths reported by the Windows loader.</returns>
    public static unsafe IReadOnlyList<string> GetCurrentModulePaths()
    {
        var modules = new nint[MaximumModules];
        uint bytesNeeded;
        fixed (nint* modulePointer = modules)
        {
            var bufferBytes = checked((uint)(modules.Length * sizeof(nint)));
            if (!K32EnumProcessModules(CurrentProcess, modulePointer, bufferBytes, out bytesNeeded))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate process modules.");
            if (bytesNeeded > bufferBytes)
                throw new InvalidOperationException($"The process loaded more than {MaximumModules} modules.");
        }

        if (bytesNeeded % sizeof(nint) != 0)
            throw new InvalidDataException("Windows returned a partial process-module handle.");

        var count = checked((int)(bytesNeeded / sizeof(nint)));
        var paths = new string[count];
        for (var index = 0; index < count; index++)
            paths[index] = GetModulePath(modules[index]);
        return paths;
    }

    /// <summary>Returns the absolute path for one loaded module handle.</summary>
    /// <param name="module">A module handle in the current process.</param>
    /// <returns>The module path reported by the Windows loader.</returns>
    public static unsafe string GetModulePath(nint module)
    {
        if (module == 0)
            throw new ArgumentException("A module handle is required.", nameof(module));

        var buffer = new char[MaximumPathLength];
        fixed (char* bufferPointer = buffer)
        {
            var length = K32GetModuleFileNameExW(
                CurrentProcess,
                module,
                bufferPointer,
                checked((uint)buffer.Length));
            if (length == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read a process-module path.");
            if (length >= buffer.Length)
                throw new PathTooLongException("A process-module path exceeded the Windows maximum path length.");
            return new string(bufferPointer, 0, checked((int)length));
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool K32EnumProcessModules(
        nint process,
        nint* modules,
        uint bufferBytes,
        out uint bytesNeeded);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint K32GetModuleFileNameExW(
        nint process,
        nint module,
        char* fileName,
        uint size);
}
