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
    private readonly string _publicKeyArmored;
    private readonly string _tempDir;

    public GpgManifestVerifierTests()
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var pgpPair = new PgpKeyPair(PublicKeyAlgorithmTag.RsaGeneral, generator.GenerateKeyPair(), DateTime.UtcNow);
        _secretKey = new PgpSecretKey(
            PgpSignature.DefaultCertification, pgpPair, "release-test@thisismypc",
            SymmetricKeyAlgorithmTag.Aes256, Passphrase, true, null, null, new SecureRandom());

        using var keyOut = new MemoryStream();
        using (var armor = new ArmoredOutputStream(keyOut))
        {
            _secretKey.PublicKey.Encode(armor);
        }
        _publicKeyArmored = Encoding.ASCII.GetString(keyOut.ToArray());

        _tempDir = Path.Combine(Path.GetTempPath(), $"tipc-gpg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private byte[] SignDetachedArmored(byte[] content)
    {
        var signatureGenerator = new PgpSignatureGenerator(PublicKeyAlgorithmTag.RsaGeneral, HashAlgorithmTag.Sha256);
        signatureGenerator.InitSign(PgpSignature.BinaryDocument, _secretKey.ExtractPrivateKey(Passphrase));
        signatureGenerator.Update(content);

        using var sigOut = new MemoryStream();
        using (var armor = new ArmoredOutputStream(sigOut))
        {
            signatureGenerator.Generate().Encode(armor);
        }
        return sigOut.ToArray();
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
        byte[]? manifest, byte[]? signature, string? publicKey = null)
    {
        return new GpgManifestUpdateVerifier(
            publicKeyArmored: publicKey ?? _publicKeyArmored,
            fetchAsync: (uri, _) => Task.FromResult(
                uri.AbsolutePath.EndsWith(".asc", StringComparison.Ordinal) ? signature : manifest));
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
        var verifier = CreateVerifier(manifest, SignDetachedArmored(manifest), publicKey: "");

        var result = await verifier.VerifyPackageAsync("1.0.0", path);

        Assert.False(result.IsSuccess);
        Assert.Contains("public key", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionBuild_StillHasNoEmbeddedKey_UntilTheCeremony()
    {
        // Flip this assertion when the release key ceremony lands the real key:
        // it exists so embedding a key is a deliberate, reviewed act.
        Assert.Equal("", GpgManifestUpdateVerifier.ReleasePublicKeyArmored);
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
