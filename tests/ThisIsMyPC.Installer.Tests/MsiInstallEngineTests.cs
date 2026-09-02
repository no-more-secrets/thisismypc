using ThisIsMyPC.Installer.Services;

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
}
