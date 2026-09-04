using ThisIsMyPC.Installer.Services;
using System.Security.Cryptography;

namespace ThisIsMyPC.Installer.Tests;

public class MsiInstallEngineTests
{
    [Fact]
    public void BuildMsiExecArguments_QuietNoRestartFolderAndLog()
    {
        var args = MsiInstallEngine.BuildMsiExecArguments(
            @"C:\Temp\x\ThisIsMyPC-win.msi", @"C:\Program Files\NMS\ThisIsMyPC", @"C:\ProgramData\ThisIsMyPC\logs\install.log");

        Assert.Equal(
            @"/i ""C:\Temp\x\ThisIsMyPC-win.msi"" /qn /norestart VELOPACK_INSTALLDIR=""C:\Program Files\NMS\ThisIsMyPC"" /l*v ""C:\ProgramData\ThisIsMyPC\logs\install.log""",
            args);
    }

    [Fact]
    public void BuildMsiExecArguments_StripsTrailingSeparatorThatWouldEscapeTheQuote()
    {
        var args = MsiInstallEngine.BuildMsiExecArguments(@"C:\a.msi", @"D:\Apps\ThisIsMyPC\", @"C:\log.txt");
        Assert.Contains(@"VELOPACK_INSTALLDIR=""D:\Apps\ThisIsMyPC""", args);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(3010, true, true)]
    [InlineData(1602, false, false)]
    [InlineData(1603, false, false)]
    [InlineData(1638, false, false)]
    [InlineData(4242, false, false)]
    public void Describe_MapsExitCodes(int code, bool succeeded, bool reboot)
    {
        var result = MsiExitCodes.Describe(code);
        Assert.Equal(succeeded, result.Succeeded);
        Assert.Equal(reboot, result.RebootRequired);
        if (succeeded)
            Assert.Null(result.Message);
        else
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Describe_AlreadyInstalled_TellsTheUserWhereToRemoveIt()
    {
        Assert.Contains("Installed apps", MsiExitCodes.Describe(MsiExitCodes.AlreadyInstalled).Message);
    }

    [Fact]
    public void EmbeddedPackage_DevBuildHasLicenseButNoMsi()
    {
        var package = new EmbeddedPackage();
        Assert.False(package.IsPresent);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", EmbeddedPackage.LoadLicenseText());
        Assert.Contains("Version 2, June 1991", EmbeddedPackage.LoadLicenseText());
    }

    [Fact]
    public void EmbeddedPackage_AppendedPayloadExtractsAndVerifies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tipc-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var bundle = Path.Combine(root, "installer.exe");
            var payload = "signed MSI payload"u8.ToArray();
            CreateBundle(bundle, payload);

            var package = new EmbeddedPackage(bundle);
            Assert.True(package.IsPresent);
            var extracted = package.ExtractTo(Path.Combine(root, "out"));
            Assert.Equal(payload, File.ReadAllBytes(extracted));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EmbeddedPackage_TamperedPayloadIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tipc-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var bundle = Path.Combine(root, "installer.exe");
            CreateBundle(bundle, "original"u8.ToArray());
            using (var stream = File.OpenWrite(bundle))
            {
                stream.Position = new FileInfo(typeof(EmbeddedPackage).Assembly.Location).Length;
                stream.WriteByte(0xFF);
            }

            var package = new EmbeddedPackage(bundle);
            Assert.Throws<InvalidDataException>(() => package.ExtractTo(Path.Combine(root, "out")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void CreateBundle(string path, byte[] payload)
    {
        var stub = File.ReadAllBytes(typeof(EmbeddedPackage).Assembly.Location);
        var footer = new byte[72];
        "TIPC-MSI-PAYLOAD"u8.CopyTo(footer);
        BitConverter.GetBytes((uint)1).CopyTo(footer, 16);
        BitConverter.GetBytes((ulong)stub.Length).CopyTo(footer, 24);
        BitConverter.GetBytes((ulong)payload.Length).CopyTo(footer, 32);
        SHA256.HashData(payload).CopyTo(footer, 40);
        var padding = (8 - ((stub.Length + payload.Length + footer.Length) % 8)) % 8;

        using var output = File.Create(path);
        output.Write(stub);
        output.Write(payload);
        output.Write(new byte[padding]);
        output.Write(footer);
    }
}
