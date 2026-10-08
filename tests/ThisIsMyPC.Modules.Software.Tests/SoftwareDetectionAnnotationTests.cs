using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Search;
using ThisIsMyPC.Interop.Win32.Packages;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;
using ThisIsMyPC.Modules.Software.Tests.Fakes;

namespace ThisIsMyPC.Modules.Software.Tests;

public sealed class SoftwareDetectionAnnotationTests
{
    [Theory]
    [InlineData("\"C:\\Apps\\app.dll\",-123", "C:\\Apps\\app.dll", -123)]
    [InlineData("C:\\Apps\\app.exe,2", "C:\\Apps\\app.exe", 2)]
    [InlineData("C:\\Apps\\app.exe", "C:\\Apps\\app.exe", 0)]
    public void RegisteredIconsPreserveResourceIndex(string value, string path, int index)
        => Assert.Equal((path, index), InstalledAppIconReader.ParseIconLocation(value));

    [Theory]
    [InlineData("zoom", "Zoom.Zoom.EXE", "Zoom Workplace", true)]
    [InlineData("zoom", "Zoom.Zoom", "Zoom Workplace", true)]
    [InlineData("zoom", "Zoom.ZoomVDI", "Zoom Workplace", false)]
    [InlineData("chatgpt", "", "ChatGPT", true)]
    [InlineData("codex", "", "ChatGPT", false)]
    [InlineData("powershell", "", "Windows PowerShell", false)]
    [InlineData("powershell", "", "PowerShell", true)]
    public void ExactAppVariantsMatchWithoutConfusingOtherProducts(string id, string packageId, string name, bool expected)
    {
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == id);
        Assert.Equal(expected, InstalledSoftwareMatcher.Matches(entry, [new(packageId, "7.5.0", name)]));
    }

    [Fact]
    public async Task FailedInventoryPreventsUninstall()
    {
        var winget = new FakeWingetService { ListFails = true };
        var module = new SoftwareModule(winget, new FakeAppxPackageService());
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == "codex");
        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateUninstall(entry));
        Assert.False(result.IsSuccess);
        Assert.Empty(winget.Uninstalls);
    }

    [Fact]
    public void RuntimeAndNpmDetectionNeedsInstalledFiles_NotJustFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "tipc-inventory-" + Guid.NewGuid().ToString("N"));
        try
        {
            var desktop = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App", "10.0.12");
            var npm = Path.Combine(root, "node_modules", "@openai", "codex");
            Directory.CreateDirectory(desktop);
            Directory.CreateDirectory(Path.Combine(npm, "bin"));
            Assert.Empty(SupplementalSoftwareInventory.ReadLocations([root], [root]));
            File.WriteAllText(Path.Combine(desktop, "PresentationFramework.dll"), "fixture");
            File.WriteAllText(Path.Combine(npm, "package.json"), "{\"name\":\"@openai/codex\",\"version\":\"0.153.4\"}");
            File.WriteAllText(Path.Combine(npm, "bin", "codex.js"), "throw new Error('must never execute');");
            var packages = SupplementalSoftwareInventory.ReadLocations([root], [root]);
            Assert.Contains(packages, p => p.PackageId == "Microsoft.DotNet.DesktopRuntime.10" && p.Version == "10.0.12");
            Assert.Contains(packages, p => p.PackageId == "OpenAI.Codex" && p.Version == "0.153.4");
            Assert.All(packages, p => Assert.False(p.CanUninstall));
            File.WriteAllText(Path.Combine(npm, "package.json"), "invalid json");
            Assert.Single(SupplementalSoftwareInventory.ReadLocations([root], [root]));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExternalInstallsAreVisibleButCannotQueueWingetUninstall()
    {
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == "codex");
        var winget = new FakeWingetService();
        winget.InstalledPackages.Add(new(entry.WingetId, "0.153.4") { CanUninstall = false });
        var module = new SoftwareModule(winget, new FakeAppxPackageService());
        var scan = Assert.IsType<SoftwareScanData>((await module.ScanSystemStateAsync()).Value);
        Assert.Contains(entry.WingetId, scan.InstalledWingetIds);
        Assert.Contains(entry.WingetId, scan.ExternallyManagedIds);
        Assert.False((await module.ExecuteActionAsync(SoftwareActionFactory.CreateUninstall(entry))).IsSuccess);
        Assert.Empty(winget.Uninstalls);
        winget.InstalledPackages.Add(new(entry.WingetId, "0.154.0"));
        Assert.True(InstalledSoftwareMatcher.FindMatch(entry, winget.InstalledPackages)!.CanUninstall);
    }

    [Fact]
    public void GlobalSearchIncludesCatalogAndWindowsApps()
    {
        var search = new SettingsSearchService([new SoftwareSearchContributor()], _ => (true, null));
        Assert.Contains(search.Search("Inkscape"), r => r.Entry.SettingId == "catalog:inkscape");
        Assert.Contains(search.Search("Calculator"), r => r.Entry.SettingId.StartsWith("appx:", StringComparison.Ordinal));
        Assert.Equal("Multimedia Tools", SoftwareCatalog.Entries.Single(e => e.Id == "inkscape").Category);
    }
}
