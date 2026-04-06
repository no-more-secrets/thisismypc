namespace ThisIsMyPC.Interop.Win32.Security;

internal enum SeObjectType : uint
{
    SeFileObject = 1,
}

[Flags]
internal enum SecurityInformationFlags : uint
{
    DaclSecurityInformation = 0x04,
    ProtectedDaclSecurityInformation = 0x80000000,
}

internal enum AccessMode : uint
{
    SetAccess = 2,
}

internal enum TrusteeForm : uint
{
    TrusteeIsName = 1,
}

internal enum TrusteeType : uint
{
    TrusteeIsWellKnownGroup = 5,
}

internal enum MultipleTrusteeOperation : uint
{
    NoMultipleTrustee = 0,
}

internal enum AclInformationClass : uint
{
    AclSizeInformation = 2,
}
