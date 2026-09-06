using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// Enables Arbitrary Code Guard for the current process. The policy forbids
/// new executable memory and changes to existing executable memory.
/// </summary>
public static partial class DynamicCodeHardening
{
    private const int ProcessDynamicCodePolicy = 2;
    private const uint ProhibitDynamicCode = 0x00000001;

    /// <summary>Enables ACG without thread opt-outs or remote downgrade.</summary>
    public static bool Apply()
    {
        var policy = ProhibitDynamicCode;
        return SetProcessMitigationPolicy(ProcessDynamicCodePolicy, ref policy, sizeof(uint));
    }

    /// <summary>Returns true when ACG prohibits dynamic code in this process.</summary>
    public static bool IsEnabled()
    {
        uint policy = 0;
        return GetProcessMitigationPolicy(
            new IntPtr(-1), ProcessDynamicCodePolicy, ref policy, sizeof(uint)) &&
            (policy & ProhibitDynamicCode) != 0;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessMitigationPolicy(
        int mitigationPolicy,
        ref uint buffer,
        nuint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMitigationPolicy(
        IntPtr process,
        int mitigationPolicy,
        ref uint buffer,
        nuint length);
}
