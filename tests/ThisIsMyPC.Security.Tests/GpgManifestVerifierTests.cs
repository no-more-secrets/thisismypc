using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Security;
using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Updates;

namespace ThisIsMyPC.Security.Tests;

/// <summary>
/// GPG manifest update verification (tm2:54). A test keypair generated per run
/// signs SHA256SUMS content; the verifier must accept only a correctly signed
/// manifest whose digest matches the package, and reject everything else.
/// </summary>
[Trait("Category", "Security")]
public sealed class GpgManifestVerifierTests : IDisposable
{
    private static readonly char[] Passphrase = ['t', 'e', 's', 't'];

    private readonly PgpSecretKey _secretKey;
    private readonly string _publicKeysArmored;
    private readonly string _tempDir;

    public GpgManifestVerifierTests()
        : this(DateTime.UtcNow, validSeconds: null)
    {
    }

    private GpgManifestVerifierTests(DateTime creationTime, long? validSeconds)
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var pgpPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, generator.GenerateKeyPair(), creationTime);
        PgpSignatureSubpacketVector? hashedPackets = null;
        if (validSeconds.HasValue)
        {
            var packets = new PgpSignatureSubpacketGenerator();
            packets.SetKeyExpirationTime(isCritical: false, validSeconds.Value);
            hashedPackets = packets.Generate();
        }

        _secretKey = new PgpSecretKey(
            PgpSignature.DefaultCertification, pgpPair, "release-test@thisismypc",
            SymmetricKeyAlgorithmTag.Aes256, Passphrase, true, hashedPackets, null, new SecureRandom());

        _publicKeysArmored = ArmorPublicKeys(_secretKey.PublicKey);

        _tempDir = Path.Combine(Path.GetTempPath(), $"tipc-gpg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private byte[] SignDetachedArmored(
        byte[] content,
        HashAlgorithmTag hashAlgorithm = HashAlgorithmTag.Sha256,
        PgpPrivateKey? privateKey = null,
        PublicKeyAlgorithmTag publicKeyAlgorithm = PublicKeyAlgorithmTag.RsaGeneral)
    {
        var signatureGenerator = new PgpSignatureGenerator(publicKeyAlgorithm, hashAlgorithm);
        signatureGenerator.InitSign(
            PgpSignature.BinaryDocument,
            privateKey ?? _secretKey.ExtractPrivateKey(Passphrase));
        signatureGenerator.Update(content);

        using var sigOut = new MemoryStream();
        using (var armor = new ArmoredOutputStream(sigOut))
        {
            signatureGenerator.Generate().Encode(armor);
        }
        return sigOut.ToArray();
    }

    private PgpPublicKey CreateRevokedPublicKey()
    {
        var signatureGenerator = new PgpSignatureGenerator(
            PublicKeyAlgorithmTag.RsaGeneral,
            HashAlgorithmTag.Sha256);
        signatureGenerator.InitSign(
            PgpSignature.KeyRevocation,
            _secretKey.ExtractPrivateKey(Passphrase));
        var revocation = signatureGenerator.GenerateCertification(_secretKey.PublicKey);
        return PgpPublicKey.AddCertification(_secretKey.PublicKey, revocation);
    }

    private static string ArmorPublicKeys(params PgpPublicKey[] publicKeys)
    {
        using var keyOut = new MemoryStream();
        using (var armor = new ArmoredOutputStream(keyOut))
        {
            foreach (var publicKey in publicKeys)
            {
                publicKey.Encode(armor);
            }
        }

        return Encoding.ASCII.GetString(keyOut.ToArray());
    }

    private static string ArmorPublicKeyRing(PgpPublicKeyRing publicKeyRing)
    {
        using var keyOut = new MemoryStream();
        using (var armor = new ArmoredOutputStream(keyOut))
        {
            publicKeyRing.Encode(armor);
        }

        return Encoding.ASCII.GetString(keyOut.ToArray());
    }

    private string WritePackage(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] ManifestFor(string fileName, byte[] packageContent) =>
        Encoding.UTF8.GetBytes(
            $"{Convert.ToHexStringLower(SHA256.HashData(packageContent))}  {fileName}\n");

    private GpgManifestUpdateVerifier CreateVerifier(
        byte[]? manifest,
        byte[]? signature,
        string? publicKeys = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        return new GpgManifestUpdateVerifier(
            publicKeysArmored: publicKeys ?? _publicKeysArmored,
            fetchAsync: (uri, _) => Task.FromResult(
                uri.AbsolutePath.EndsWith(".asc", StringComparison.Ordinal) ? signature : manifest),
            utcNow: utcNow);
    }

    [Fact]
    public async Task SignedManifestWithMatchingDigest_Passes()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        var verifier = CreateVerifier(manifest, SignDetachedArmored(manifest));

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TamperedPackage_IsRejected()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        // The manifest is honestly signed, but the downloaded bytes differ.
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", Encoding.UTF8.GetBytes("evil payload"));
        var verifier = CreateVerifier(manifest, SignDetachedArmored(manifest));

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not match", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TamperedManifest_FailsSignatureCheck()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var honest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", Encoding.UTF8.GetBytes("other bytes"));
        var forged = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        // Signature made over the honest manifest, served with a forged one.
        var verifier = CreateVerifier(forged, SignDetachedArmored(honest));

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
        Assert.Contains("signature", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignatureFromAForeignKey_IsRejected()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);

        // A different keypair plays "the attacker's key the build does not trust".
        using var foreign = new GpgManifestVerifierTests();
        var verifier = CreateVerifier(manifest, foreign.SignDetachedArmored(manifest));

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task SignatureFromEitherTrustedKey_Passes()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        using var secondSigner = new GpgManifestVerifierTests();
        var trustedKeys = ArmorPublicKeys(_secretKey.PublicKey, secondSigner._secretKey.PublicKey);
        var verifier = CreateVerifier(
            manifest,
            secondSigner.SignDetachedArmored(manifest),
            trustedKeys);

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SignatureFromExpiredTrustedKey_IsRejected()
    {
        using var expiredSigner = new GpgManifestVerifierTests(
            DateTime.UtcNow.AddDays(-2),
            validSeconds: 60);
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        var verifier = CreateVerifier(
            manifest,
            expiredSigner.SignDetachedArmored(manifest),
            expiredSigner._publicKeysArmored);

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task SignatureFromRevokedTrustedKey_IsRejected()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        var revokedPublicKey = ArmorPublicKeys(CreateRevokedPublicKey());
        var verifier = CreateVerifier(
            manifest,
            SignDetachedArmored(manifest),
            revokedPublicKey);

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task SignatureUsingSha1_IsRejected()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        var verifier = CreateVerifier(
            manifest,
            SignDetachedArmored(manifest, HashAlgorithmTag.Sha1));

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task SignatureFromTrustedSigningSubkey_IsRejected()
    {
        var random = new SecureRandom();
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(random, 2048));
        var primaryPair = new PgpKeyPair(
            PublicKeyAlgorithmTag.RsaGeneral,
            generator.GenerateKeyPair(),
            DateTime.UtcNow);
        var subkeyPair = new PgpKeyPair(
            PublicKeyAlgorithmTag.RsaSign,
            generator.GenerateKeyPair(),
            DateTime.UtcNow);
        var keyRingGenerator = new PgpKeyRingGenerator(
            PgpSignature.DefaultCertification,
            primaryPair,
            "release-test@thisismypc",
            SymmetricKeyAlgorithmTag.Aes256,
            HashAlgorithmTag.Sha256,
            Passphrase,
            useSha1: true,
            hashedPackets: null,
            unhashedPackets: null,
            random);
        var subkeyPackets = new PgpSignatureSubpacketGenerator();
        subkeyPackets.SetKeyFlags(isCritical: true, PgpKeyFlags.CanSign);
        keyRingGenerator.AddSubKey(subkeyPair, subkeyPackets.Generate(), unhashedPackets: null);

        var secretKeyRing = keyRingGenerator.GenerateSecretKeyRing();
        var subkeyPrivateKey = secretKeyRing.GetSecretKey(subkeyPair.KeyId).ExtractPrivateKey(Passphrase);
        var trustedKeyRing = ArmorPublicKeyRing(keyRingGenerator.GeneratePublicKeyRing());
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        var signature = SignDetachedArmored(
            manifest,
            privateKey: subkeyPrivateKey,
            publicKeyAlgorithm: PublicKeyAlgorithmTag.RsaSign);
        var verifier = CreateVerifier(manifest, signature, trustedKeyRing);

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DowngradeReplay_PackageNameWithoutTargetVersion_IsRejected()
    {
        // A genuinely signed OLD release (manifest and package both authentic)
        // replayed under a new version tag must not verify as the new version.
        var oldPackage = Encoding.UTF8.GetBytes("old release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", oldPackage);
        var oldManifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", oldPackage);
        var verifier = CreateVerifier(oldManifest, SignDetachedArmored(oldManifest));

        var result = await verifier.VerifyPackageAsync("2.0.0", path);

        Assert.False(result.IsSuccess);
        Assert.Contains("downgrade", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManifestWithUtf8Bom_StillVerifies()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = Encoding.UTF8.GetPreamble()
            .Concat(ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package))
            .ToArray();
        var verifier = CreateVerifier(manifest, SignDetachedArmored(manifest));

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task MissingManifest_IsRejected()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var verifier = CreateVerifier(manifest: null, signature: null);

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PackageAbsentFromManifest_IsRejected()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("SomethingElse.nupkg", package);
        var verifier = CreateVerifier(manifest, SignDetachedArmored(manifest));

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not list", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnresolvedPackagePath_IsRejected_NeverSkipped()
    {
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", Encoding.UTF8.GetBytes("x"));
        var verifier = CreateVerifier(manifest, SignDetachedArmored(manifest));

        var result = await verifier.VerifyPackageAsync("1.0.0", packageFilePath: null);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task EmptyEmbeddedKey_RejectsEverything()
    {
        var package = Encoding.UTF8.GetBytes("release payload");
        var path = WritePackage("ThisIsMyPC-1.0.0-full.nupkg", package);
        var manifest = ManifestFor("ThisIsMyPC-1.0.0-full.nupkg", package);
        var verifier = CreateVerifier(manifest, SignDetachedArmored(manifest), publicKeys: "");

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
        Assert.Contains("public key", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionBuild_ContainsBothCeremonyKeys()
    {
        using var keyStream = PgpUtilities.GetDecoderStream(
            new MemoryStream(Encoding.ASCII.GetBytes(
                GpgManifestUpdateVerifier.ReleasePublicKeysArmored)));
        var keyBundle = new PgpPublicKeyRingBundle(keyStream);
        var fingerprints = keyBundle.GetKeyRings()
            .Select(keyRing => Convert.ToHexString(keyRing.GetPublicKey().GetFingerprint()))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, keyBundle.Count);
        Assert.Equal(
        [
            "3D253EF5CB19A049F705FE2836D91EDA85049BED",
            "91C8A2E194EFFAC5E1DD3DB9BD46E205F028C7C0",
        ], fingerprints);
    }

    [Fact]
    public void ReleaseManifest_ParsesShaSumFormats()
    {
        var digest = new string('a', 64);
        var manifest = ReleaseManifest.TryParse(
            $"{digest}  first.nupkg\r\n{digest} *second.exe\n\n");

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Count);
        Assert.Equal(digest, manifest.DigestFor("first.nupkg"));
        Assert.Equal(digest, manifest.DigestFor("second.exe"));
        Assert.Null(manifest.DigestFor("absent.bin"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a manifest")]
    [InlineData("zzzz  file.nupkg")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  ..\\evil.exe")]
    public void ReleaseManifest_RejectsMalformedContent(string content)
    {
        Assert.Null(ReleaseManifest.TryParse(content));
    }
}
