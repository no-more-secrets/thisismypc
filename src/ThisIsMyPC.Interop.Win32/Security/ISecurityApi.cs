namespace ThisIsMyPC.Interop.Win32.Security;

public interface ISecurityApi
{
    /// <summary>
    /// Applies a restrictive DACL to a directory, granting access only to the specified trustees.
    /// Disables inheritance when <paramref name="disableInheritance"/> is true.
    /// </summary>
    /// <returns>Win32 error code (0 = ERROR_SUCCESS).</returns>
    uint ApplyDacl(string directoryPath, DaclAccessEntry[] entries, bool disableInheritance);

    /// <summary>
    /// Reads the current DACL on a directory and returns structured info for verification.
    /// </summary>
    DaclInfo ReadDacl(string directoryPath);
}

public sealed record DaclAccessEntry(string TrusteeName, uint AccessPermissions, uint Inheritance);

public sealed record DaclInfo(bool IsProtected, IReadOnlyList<AceInfo> Entries, string? Error = null)
{
    public static DaclInfo Failure(string error) => new(false, [], error);
}

public sealed record AceInfo(string Sid, uint AccessMask);
