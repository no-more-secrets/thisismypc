using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using NLog;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Updates;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Out-of-band update verification per the threat model (tm2:54): each release
/// publishes SHA256SUMS plus a detached armored signature SHA256SUMS.asc made by
/// either offline release key. Both public keys are embedded in the app, so a
/// compromised GitHub account plus a compromised code-signing cert cannot forge
/// an update. Fail-closed everywhere: no manifest, bad signature, unknown file,
/// digest mismatch, or an unresolved package path all reject the update.
/// </summary>
public sealed class GpgManifestUpdateVerifier : IUpdateVerifier
{
    private const string ReleasePublicKeysResourceName = "ThisIsMyPC.ReleasePublicKeys.asc";

    private static readonly IReadOnlySet<string> ReleaseSigningKeyFingerprints =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "3D253EF5CB19A049F705FE2836D91EDA85049BED",
            "91C8A2E194EFFAC5E1DD3DB9BD46E205F028C7C0",
        };

    /// <summary>
    /// The ASCII-armored release public key ring. It contains one independent
    /// key from each release YubiKey. A signature from either key is accepted.
    /// </summary>
    public static string ReleasePublicKeysArmored { get; } = LoadReleasePublicKeys();

    /// <summary>
    /// Release assets live under the tag; tags are v{version}. Derived from
    /// AppConstants.UpdateUrl so a repo rename cannot leave the verifier
    /// fetching from a stale (and eventually squattable) owner name.
    /// </summary>
    private static readonly string ManifestUrlFormat =
        Core.AppConstants.UpdateUrl + "/download/v{0}/SHA256SUMS";

    private const int MaxManifestBytes = 1024 * 1024;

    private static readonly HttpClient SharedHttp = CreateHttpClient();

    private readonly string _publicKeysArmored;
    private readonly IReadOnlySet<string> _trustedSigningKeyFingerprints;
    private readonly Func<Uri, CancellationToken, Task<byte[]?>> _fetchAsync;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly ILogger _logger;

    public GpgManifestUpdateVerifier(
        string? publicKeysArmored = null,
        Func<Uri, CancellationToken, Task<byte[]?>>? fetchAsync = null,
        ILogger? logger = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _publicKeysArmored = publicKeysArmored ?? ReleasePublicKeysArmored;
        _trustedSigningKeyFingerprints = publicKeysArmored is null
            ? ReleaseSigningKeyFingerprints
            : ReadPrimaryFingerprints(_publicKeysArmored);
        _fetchAsync = fetchAsync ?? FetchOverHttpAsync;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
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

            if (string.IsNullOrWhiteSpace(_publicKeysArmored))
            {
                return Reject(updateVersion,
                    "No release public keys are embedded in this build; updates cannot be verified.");
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
                return Reject(updateVersion, "The release manifest signature does not match a release key.");

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

    /// <summary>True only when the detached signature verifies against an active trusted primary key.</summary>
    private bool VerifyDetachedSignature(byte[] manifestBytes, byte[] signatureBytes)
    {
        using var keyStream = PgpUtilities.GetDecoderStream(
            new MemoryStream(Encoding.ASCII.GetBytes(_publicKeysArmored)));
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
            if (!IsStrongHashAlgorithm(signature.HashAlgorithm))
            {
                _logger.Warn("SHA256SUMS.asc used a disallowed hash algorithm: {Algorithm}",
                    signature.HashAlgorithm);
                continue;
            }

            var key = publicKeys.GetPublicKey(signature.KeyId);
            if (key is null || !IsActiveTrustedPrimaryKey(key))
                continue;

            signature.InitVerify(key);
            signature.Update(manifestBytes);
            if (signature.Verify())
                return true;
        }

        return false;
    }

    private static bool IsStrongHashAlgorithm(HashAlgorithmTag algorithm) =>
        algorithm is HashAlgorithmTag.Sha256 or HashAlgorithmTag.Sha384 or HashAlgorithmTag.Sha512;

    private bool IsActiveTrustedPrimaryKey(PgpPublicKey key)
    {
        if (!key.IsMasterKey
            || !_trustedSigningKeyFingerprints.Contains(Convert.ToHexString(key.GetFingerprint()))
            || key.HasRevocation())
        {
            return false;
        }

        var now = _utcNow();
        var created = new DateTimeOffset(key.CreationTime.ToUniversalTime());
        if (now < created)
            return false;

        var validSeconds = key.GetValidSeconds();
        return validSeconds == 0 || now < created.AddSeconds(validSeconds);
    }

    private static HashSet<string> ReadPrimaryFingerprints(string publicKeysArmored)
    {
        if (string.IsNullOrWhiteSpace(publicKeysArmored))
            return new HashSet<string>(StringComparer.Ordinal);

        using var keyStream = PgpUtilities.GetDecoderStream(
            new MemoryStream(Encoding.ASCII.GetBytes(publicKeysArmored)));
        var publicKeys = new PgpPublicKeyRingBundle(keyStream);
        return publicKeys.GetKeyRings()
            .Select(keyRing => Convert.ToHexString(keyRing.GetPublicKey().GetFingerprint()))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string LoadReleasePublicKeys()
    {
        using var stream = typeof(GpgManifestUpdateVerifier).Assembly
            .GetManifestResourceStream(ReleasePublicKeysResourceName);
        if (stream is null)
            return string.Empty;

        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
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
