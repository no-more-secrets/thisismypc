using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Security;

/// <summary>
/// WinVerifyTrust gate for child processes this elevated app launches from
/// locations a standard user can write to (the winget app-execution alias lives
/// under the user profile). A tampered binary there would otherwise run with
/// our token: classic user-to-admin escalation. Full Authenticode chain
/// verification plus an optional signer-subject substring or exact simple-name
/// check.
/// </summary>
public static partial class AuthenticodeVerifier
{
    /// <summary>
    /// Succeeds only when <paramref name="filePath"/> carries a valid, trusted
    /// Authenticode signature, and (when given) the signer subject contains
    /// <paramref name="requiredSubjectFragment"/>. The default is an ordinal,
    /// case-insensitive subject substring. <paramref name="exactSignerName"/>
    /// instead requires an ordinal match against the certificate's simple name.
    /// </summary>
    public static OperationResult<bool> VerifyTrusted(
        string filePath,
        string? requiredSubjectFragment = null,
        bool exactSignerName = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        try
        {
            if (!File.Exists(filePath))
                return OperationResult<bool>.Failure($"File not found: {filePath}", ErrorCategory.NotFound);

            var trust = WinVerifyTrustFile(filePath);
            if (trust != 0)
            {
                return OperationResult<bool>.Failure(
                    $"{Path.GetFileName(filePath)} is not signed by a trusted publisher (WinVerifyTrust=0x{trust:X8}).",
                    ErrorCategory.AccessDenied);
            }

            if (requiredSubjectFragment is not null)
            {
                using var signer = ReadSignerCertificate(filePath);
                var signerName = signer?.GetNameInfo(X509NameType.SimpleName, false);
                var matches = exactSignerName
                    ? string.Equals(signerName, requiredSubjectFragment, StringComparison.Ordinal)
                    : signer?.Subject.Contains(requiredSubjectFragment, StringComparison.OrdinalIgnoreCase) == true;
                if (!matches)
                {
                    return OperationResult<bool>.Failure(
                        $"{Path.GetFileName(filePath)} is signed, but not by '{requiredSubjectFragment}' (signer: {signerName ?? "unknown"}).",
                        ErrorCategory.AccessDenied);
                }
            }

            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return OperationResult<bool>.Failure(
                $"Signature verification failed for {filePath}: {ex.Message}", ErrorCategory.AccessDenied, ex);
        }
    }

    private static X509Certificate2? ReadSignerCertificate(string filePath)
    {
        try
        {
            return new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
        }
        catch (CryptographicException)
        {
            return null; // WinVerifyTrust already passed; a read failure here still rejects the subject check
        }
    }

    // ---- WinVerifyTrust plumbing (wintrust.dll) ----

    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    /// <summary>Raw WinVerifyTrust result for an embedded signature: 0 verified, TRUST_E_NOSIGNATURE for none, other codes for a bad chain.</summary>
    internal static unsafe int WinVerifyTrustFile(string filePath)
    {
        fixed (char* pathPtr = filePath)
        {
            var fileInfo = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                pcwszFilePath = (nint)pathPtr,
            };

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = (nint)(&fileInfo),
                dwStateAction = WTD_STATEACTION_VERIFY,
                // Cached revocation only: verification must not hang launches offline.
                dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
            };

            var actionId = ActionGenericVerifyV2;
            var result = WinVerifyTrust(0, ref actionId, &data);

            data.dwStateAction = WTD_STATEACTION_CLOSE;
            _ = WinVerifyTrust(0, ref actionId, &data);

            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public nint pcwszFilePath;
        public nint hFile;
        public nint pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public nint pFile;
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    private static unsafe partial int WinVerifyTrust(nint hwnd, ref Guid actionId, WINTRUST_DATA* pWVTData);
}
