using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg.OpenPgp;
using NLog;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Updates;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Out-of-band update verification per the threat model (tm2:54): each release
/// publishes SHA256SUMS plus a detached armored signature SHA256SUMS.asc made by
/// the offline release key. The public key is hardcoded here, so even a
/// compromised GitHub account plus a compromised code-signing cert cannot forge
/// an update. Fail-closed everywhere: no manifest, bad signature, unknown file,
/// digest mismatch, or an unresolved package path all reject the update.
/// </summary>
public sealed class GpgManifestUpdateVerifier : IUpdateVerifier
{
    /// <summary>
    /// The ASCII-armored release public key. Empty until the release key
    /// ceremony (docs/release/update-signing.md); while empty, every update is
    /// rejected, which is the correct failure direction for unsigned builds.
    /// </summary>
    public const string ReleasePublicKeyArmored = "";

    /// <summary>
    /// Release assets live under the tag; tags are v{version}. Derived from
    /// AppConstants.UpdateUrl so a repo rename cannot leave the verifier
    /// fetching from a stale (and eventually squattable) owner name.
    /// </summary>
    private static readonly string ManifestUrlFormat =
        Core.AppConstants.UpdateUrl + "/download/v{0}/SHA256SUMS";

    private const int MaxManifestBytes = 1024 * 1024;

    private static readonly HttpClient SharedHttp = CreateHttpClient();

    private readonly string _publicKeyArmored;
    private readonly Func<Uri, CancellationToken, Task<byte[]?>> _fetchAsync;
    private readonly ILogger _logger;

    public GpgManifestUpdateVerifier(
        string? publicKeyArmored = null,
        Func<Uri, CancellationToken, Task<byte[]?>>? fetchAsync = null,
        ILogger? logger = null)
    {
        _publicKeyArmored = publicKeyArmored ?? ReleasePublicKeyArmored;
        _fetchAsync = fetchAsync ?? FetchOverHttpAsync;
        _logger = logger ?? LogManager.GetLogger("ThisIsMyPC.App.Services.GpgManifestUpdateVerifier");
    }

    public async Task<OperationResult<bool>> VerifyPackageAsync(
        string updateVersion, string? packageFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(packageFilePath) || !File.Exists(packageFilePath))
            {
                return Reject(updateVersion,
                    "The downloaded update package could not be located for verification.");
            }

            if (string.IsNullOrWhiteSpace(_publicKeyArmored))
            {
                return Reject(updateVersion,
                    "No release public key is embedded in this build; updates cannot be verified.");
            }

            var manifestUri = new Uri(string.Format(CultureInfo.InvariantCulture, ManifestUrlFormat, updateVersion));
            var signatureUri = new Uri(manifestUri + ".asc");

            var manifestBytes = await _fetchAsync(manifestUri, cancellationToken).ConfigureAwait(false);
            if (manifestBytes is null or { Length: 0 } || manifestBytes.Length > MaxManifestBytes)
                return Reject(updateVersion, "The release manifest (SHA256SUMS) could not be downloaded.");

            var signatureBytes = await _fetchAsync(signatureUri, cancellationToken).ConfigureAwait(false);
            if (signatureBytes is null or { Length: 0 } || signatureBytes.Length > MaxManifestBytes)
                return Reject(updateVersion, "The release manifest signature (SHA256SUMS.asc) could not be downloaded.");

            if (!VerifyDetachedSignature(manifestBytes, signatureBytes))
                return Reject(updateVersion, "The release manifest signature does not match the release key.");

            var manifest = ReleaseManifest.TryParse(Encoding.UTF8.GetString(manifestBytes));
            if (manifest is null)
                return Reject(updateVersion, "The release manifest is malformed.");

            var fileName = Path.GetFileName(packageFilePath);

            // Version binding (anti-downgrade): a genuinely signed manifest from
            // an OLD release replayed under a new tag still verifies, so the
            // package name itself must carry the version being installed
            // (Velopack names packages {PackId}-{Version}-*.nupkg).
            if (!fileName.Contains(updateVersion, StringComparison.OrdinalIgnoreCase))
            {
                return Reject(updateVersion,
                    $"The package name {fileName} does not carry version {updateVersion}; possible downgrade replay.");
            }

            var expectedDigest = manifest.DigestFor(fileName);
            if (expectedDigest is null)
                return Reject(updateVersion, $"The release manifest does not list the package {fileName}.");

            var actualDigest = await ComputeSha256Async(packageFilePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedDigest, actualDigest, StringComparison.OrdinalIgnoreCase))
                return Reject(updateVersion, "The downloaded package does not match the signed manifest digest.");

            _logger.Info(
                "Update {Version} verified: signed manifest matched, SHA-256 {Digest} confirmed for {File}",
                updateVersion, actualDigest, fileName);
            return OperationResult<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Verification must never crash the app; any error rejects the update
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.Error(ex, "Update verification errored for {Version}", updateVersion);
            return OperationResult<bool>.Failure(
                $"Update verification error: {ex.Message}", ErrorCategory.AccessDenied, ex);
        }
    }

    private OperationResult<bool> Reject(string version, string reason)
    {
        _logger.Warn("Update {Version} rejected: {Reason}", version, reason);
        return OperationResult<bool>.Failure(reason, ErrorCategory.AccessDenied);
    }

    /// <summary>True only when the detached signature over the manifest verifies against the embedded key.</summary>
    private bool VerifyDetachedSignature(byte[] manifestBytes, byte[] signatureBytes)
    {
        using var keyStream = PgpUtilities.GetDecoderStream(
            new MemoryStream(Encoding.ASCII.GetBytes(_publicKeyArmored)));
        var publicKeys = new PgpPublicKeyRingBundle(keyStream);

        using var signatureStream = PgpUtilities.GetDecoderStream(new MemoryStream(signatureBytes));
        if (new PgpObjectFactory(signatureStream).NextPgpObject() is not PgpSignatureList signatures
            || signatures.Count == 0)
        {
            _logger.Warn("SHA256SUMS.asc did not contain a detached signature");
            return false;
        }

        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            var key = publicKeys.GetPublicKey(signature.KeyId);
            if (key is null)
                continue; // signed by a key this build does not trust

            signature.InitVerify(key);
            signature.Update(manifestBytes);
            if (signature.Verify())
                return true;
        }

        return false;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            // Oversized bodies throw before buffering past the cap; the catch
            // below turns that into a rejection.
            MaxResponseContentBufferSize = MaxManifestBytes,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ThisIsMyPC-Updater");
        return client;
    }

    private async Task<byte[]?> FetchOverHttpAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SharedHttp.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Fetching {Uri} returned {Status}", uri, response.StatusCode);
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn(ex, "Fetching {Uri} failed", uri);
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Fetching {Uri} timed out", uri);
            return null;
        }
    }
}
