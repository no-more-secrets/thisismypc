using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// Enables Code Integrity Guard for the current process. After activation,
/// Windows refuses image mappings that are not signed by Microsoft.
/// </summary>
public static partial class BinarySignatureHardening
{
    private const int ProcessSignaturePolicy = 8;
    private const uint MicrosoftSignedOnly = 0x00000001;

    public static bool Apply()
    {
        var policy = MicrosoftSignedOnly;
        return SetProcessMitigationPolicy(ProcessSignaturePolicy, ref policy, sizeof(uint));
    }

    public static bool IsEnabled()
    {
        uint policy = 0;
        return GetProcessMitigationPolicy(
            new IntPtr(-1), ProcessSignaturePolicy, ref policy, sizeof(uint)) &&
            (policy & MicrosoftSignedOnly) != 0;
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
