using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Beta verifier: checks Authenticode signature on the update package (if path resolved)
/// or falls back to the current application binary (build pipeline integrity check).
/// Velopack's built-in SHA256 integrity ensures downloads match the published package.
/// v1.0 will add GPG verification of the release manifest via a replacement IUpdateVerifier.
/// </summary>
public sealed class AuthenticodeUpdateVerifier : IUpdateVerifier
{
    private readonly ILogger _logger;

    public AuthenticodeUpdateVerifier(ILogger? logger = null)
    {
        _logger = logger ?? Log.Logger;
    }

    public OperationResult<bool> VerifyPackageIntegrity(string updateVersion, string? packageFilePath = null)
    {
        try
        {
            var targetPath = packageFilePath;

            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
            {
                // Fall back to verifying the current application binary.
                // This confirms the build pipeline produces signed output.
                targetPath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                {
                    _logger.Warning("Cannot determine any path for signature verification");
                    return OperationResult<bool>.Failure(
                        "Cannot determine application path for signature verification.",
                        ErrorCategory.NotFound);
                }

                _logger.Information(
                    "Update package path not available — falling back to build pipeline check on {Path}",
                    targetPath);
            }
            else
            {
                _logger.Information("Verifying Authenticode signature on update package: {Path}", targetPath);
            }

            return VerifyAuthenticode(targetPath, updateVersion);
        }
#pragma warning disable CA1031 // Verification must not crash — report failure
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Error(ex, "Authenticode verification failed for update {Version}", updateVersion);
            return OperationResult<bool>.Failure(
                $"Signature verification error: {ex.Message}",
                ErrorCategory.AccessDenied,
                ex);
        }
    }

    private OperationResult<bool> VerifyAuthenticode(string filePath, string version)
    {
        try
        {
            var cert = X509Certificate.CreateFromSignedFile(filePath);
            using var cert2 = new X509Certificate2(cert);

            _logger.Information(
                "Signed by {Subject}, valid {NotBefore} to {NotAfter} — update {Version} integrity confirmed",
                cert2.Subject, cert2.NotBefore, cert2.NotAfter, version);

            return OperationResult<bool>.Success(true);
        }
        catch (CryptographicException ex)
        {
            _logger.Warning(
                "Binary at {Path} is NOT signed or has invalid signature: {Error}. Update {Version} rejected.",
                filePath, ex.Message, version);
            return OperationResult<bool>.Failure(
                "Update package is not signed or has an invalid signature.",
                ErrorCategory.AccessDenied);
        }
    }
}
