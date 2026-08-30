using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

internal static partial class NativeSecurity
{
    // advapi32.dll; DACL manipulation

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SetNamedSecurityInfoW(
        string pObjectName,
        uint objectType,
        uint securityInfo,
        nint psidOwner,
        nint psidGroup,
        nint pDacl,
        nint pSacl);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetNamedSecurityInfoW(
        string pObjectName,
        uint objectType,
        uint securityInfo,
        out nint ppsidOwner,
        out nint ppsidGroup,
        out nint ppDacl,
        out nint ppSacl,
        out nint ppSecurityDescriptor);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SetEntriesInAclW(
        uint cCountOfExplicitEntries,
        [In] ExplicitAccessW[] pListOfExplicitEntries,
        nint oldAcl,
        out nint newAcl);

    // advapi32.dll; DACL verification

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetAclInformation(
        nint pAcl,
        out AclSizeInformation pAclInformation,
        uint nAclInformationLength,
        uint dwAclInformationClass);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetAce(
        nint pAcl,
        uint dwAceIndex,
        out nint pAce);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSecurityDescriptorControl(
        nint pSecurityDescriptor,
        out ushort pControl,
        out uint lpdwRevision);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ConvertSidToStringSidW(
        nint sid,
        out nint stringSid);

    // kernel32.dll; memory management

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint LocalFree(nint hMem);
}
