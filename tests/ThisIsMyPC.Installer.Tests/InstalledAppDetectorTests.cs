using Microsoft.Win32;
using ThisIsMyPC.Installer.Services;

namespace ThisIsMyPC.Installer.Tests;

public class InstalledAppDetectorTests
{
    [Fact]
    public void RegistryLocations_AreMachineScopedOnly()
    {
        var locations = InstalledAppDetector.RegistryLocations().ToArray();

        Assert.Equal(2, locations.Length);
        Assert.All(locations, location => Assert.Equal(RegistryHive.LocalMachine, location.Hive));
    }

    private const string SqVersion = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
        <metadata>
        <id>ThisIsMyPC</id>
        <version>0.1.0</version>
        <mainExe>ThisIsMyPC.App.exe</mainExe>
        </metadata>
        </package>
        """;

    [Fact]
    public void ParseVersionFile_ReadsTheVersionElement()
    {
        Assert.Equal("0.1.0", InstalledAppDetector.ParseVersionFile(SqVersion));
        Assert.Null(InstalledAppDetector.ParseVersionFile("<package/>"));
        Assert.Null(InstalledAppDetector.ParseVersionFile(""));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\NMS\ThisIsMyPC\Update.exe"" --uninstall", @"C:\Program Files\NMS\ThisIsMyPC")]
    [InlineData(@"C:\Apps\ThisIsMyPC\Update.exe uninstall", @"C:\Apps\ThisIsMyPC")]
    [InlineData(@"msiexec /x {GUID}", null)]
    [InlineData("", null)]
    public void FolderFromUninstallString_FindsTheUpdaterFolder(string uninstallString, string? expected)
    {
        Assert.Equal(expected, InstalledAppDetector.FolderFromUninstallString(uninstallString));
    }

    [Fact]
    public void FromFolder_NeedsUpdateExeAndAVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "tipc-detector-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(InstalledAppDetector.FromFolder(root));

            Directory.CreateDirectory(Path.Combine(root, "current"));
            File.WriteAllText(Path.Combine(root, "Update.exe"), "stub");
            Assert.Null(InstalledAppDetector.FromFolder(root));

            File.WriteAllText(Path.Combine(root, "current", "sq.version"), SqVersion);
            var found = InstalledAppDetector.FromFolder(root);
            Assert.NotNull(found);
            Assert.Equal("0.1.0", found.Version);
            Assert.Equal(root, found.InstallFolder);
            Assert.Equal(Path.Combine(root, "Update.exe"), found.UninstallerPath);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FromFolder_NullOrBlank_IsNotInstalled()
    {
        Assert.Null(InstalledAppDetector.FromFolder(null));
        Assert.Null(InstalledAppDetector.FromFolder("  "));
    }

    [Fact]
    public void FromFolder_DoesNotParseExecutableVersionResources()
    {
        var root = Path.Combine(Path.GetTempPath(), "tipc-detector-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "current"));
            File.WriteAllBytes(Path.Combine(root, "Update.exe"), [0xff, 0x00, 0x13, 0x37]);
            File.WriteAllBytes(Path.Combine(root, "current", "ThisIsMyPC.App.exe"), [0xff, 0x00, 0x13, 0x37]);

            Assert.Null(InstalledAppDetector.FromFolder(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FromFolder_RejectsOversizedVersionFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "tipc-detector-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "current"));
            File.WriteAllText(Path.Combine(root, "Update.exe"), "stub");
            File.WriteAllText(Path.Combine(root, "current", "sq.version"), new string('x', 65 * 1024));

            Assert.Null(InstalledAppDetector.FromFolder(root));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
