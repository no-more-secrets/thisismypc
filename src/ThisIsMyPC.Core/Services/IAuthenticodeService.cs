namespace ThisIsMyPC.Core.Services;

public enum SignatureState
{
    /// <summary>No Authenticode signature, embedded or in a Windows catalog.</summary>
    Unsigned,

    /// <summary>Signed, and the chain verifies to a trusted root.</summary>
    Verified,

    /// <summary>Signed, but the signature or its chain does not verify (self-signed, expired, tampered).</summary>
    NotVerified,

    /// <summary>The file could not be read.</summary>
    Unknown,
}

/// <summary>Who signed a file, the way Autoruns shows it: "(Verified) Microsoft Windows".</summary>
public sealed record SignatureInfo(SignatureState State, string? Signer)
{
    public static SignatureInfo Unsigned { get; } = new(SignatureState.Unsigned, null);
    public static SignatureInfo Unknown { get; } = new(SignatureState.Unknown, null);
}

/// <summary>Authenticode check for a file: its embedded signature, or the Windows security catalog that lists it.</summary>
public interface IAuthenticodeService
{
    SignatureInfo Check(string path);
}
