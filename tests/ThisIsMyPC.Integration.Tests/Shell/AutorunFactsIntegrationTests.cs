using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Security;
using ThisIsMyPC.Interop.Win32.Shell;

namespace ThisIsMyPC.Integration.Tests.Shell;

/// <summary>
/// The two facts the Autoruns page fetches per row, against real Windows
/// files: the shell icon comes back as visible pixels, and a System32 binary
/// verifies as Microsoft Windows through the catalog path. Read-only.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AutorunFactsIntegrationTests
{
    private static string System32(string file) => Path.GetFullPath(Path.Combine(Environment.SystemDirectory, file));

    [Fact]
    public void GetSmallIcon_Explorer_HasOpaquePixels()
    {
        var result = new FileIconService().GetSmallIcon(System32(@"..\explorer.exe"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var icon = result.Value!;
        Assert.True(icon.Width >= 16 && icon.Height >= 16, $"{icon.Width}x{icon.Height}");
        Assert.Equal(icon.Width * icon.Height * 4, icon.Bgra.Length);

        var opaque = 0;
        for (var i = 3; i < icon.Bgra.Length; i += 4)
        {
            if (icon.Bgra[i] > 0)
                opaque++;
        }
        Assert.True(opaque > 16, $"only {opaque} opaque pixels");
    }

    [Fact]
    public void GetSmallIcon_MissingExe_FallsBackToTheExtensionIcon()
    {
        var result = new FileIconService().GetSmallIcon(@"C:\Definitely\Not\Here\gone.exe");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotEmpty(result.Value!.Bgra);
    }

    [Fact]
    public void Check_CatalogSignedSystemFile_VerifiesAsMicrosoftWindows()
    {
        var info = new AuthenticodeService().Check(System32("svchost.exe"));

        Assert.Equal(SignatureState.Verified, info.State);
        Assert.Contains("Microsoft Windows", info.Signer, StringComparison.OrdinalIgnoreCase);
    }
}
