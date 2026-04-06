using System.Runtime.InteropServices;

namespace ThisIsMyPC.Interop.Win32.Security;

[StructLayout(LayoutKind.Sequential)]
internal struct TrusteeW
{
    public nint pMultipleTrustee;
    public uint MultipleTrusteeOperation;
    public uint TrusteeForm;
    public uint TrusteeType;
    public nint ptstrName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ExplicitAccessW
{
    public uint grfAccessPermissions;
    public uint grfAccessMode;
    public uint grfInheritance;
    public TrusteeW Trustee;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AclSizeInformation
{
    public uint AceCount;
    public uint AclBytesInUse;
    public uint AclBytesFree;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AceHeader
{
    public byte AceType;
    public byte AceFlags;
    public ushort AceSize;
}
