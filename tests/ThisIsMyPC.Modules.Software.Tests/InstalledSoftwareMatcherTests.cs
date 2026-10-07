using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Interop.Win32.Packages;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.Modules.Software.Tests;

public sealed class InstalledSoftwareMatcherTests
{
    [Theory]
    [InlineData("Python 3.13.9 (64-bit)", "3.13.9", false)]
    [InlineData("Python 3.14.3", "3.14-64", true)]
    [InlineData("Python 3.14.3 (64-bit)", "3.14.3", true)]
    [InlineData("Python", "Unknown", false)]
    public void VersionSpecificPackagesRequireTheSameVersionFamily(string name, string version, bool expected)
    {
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == "python3");
        Assert.Equal(expected, InstalledSoftwareMatcher.Matches(entry, [new("", version, name)]));
    }

    [Theory]
    [InlineData("chrome", "Google Chrome", true)]
    [InlineData("chrome", "Google Chrome Beta", false)]
    [InlineData("chrome", "Google Chrome Helper", false)]
    [InlineData("chrome", "Google Chrom…", false)]
    [InlineData("firefox", "Mozilla Firefox (x64 en-US)", true)]
    [InlineData("firefox", "Mozilla Firefox ESR (x64 en-US)", false)]
    [InlineData("7zip", "7-Zip 24.09 (x64)", true)]
    [InlineData("zoom", "Zoom Workplace 6.6.11 (23272)", true)]
    [InlineData("vc2015_64", "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.44.35211", true)]
    [InlineData("vc2015_64", "Microsoft Visual C++ 2015-2022 Redistributable (x86) - 14.44.35211", false)]
    public void UncorrelatedNamesMatchOnlyTheIntendedProduct(string id, string name, bool expected)
    {
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == id);
        Assert.Equal(expected, InstalledSoftwareMatcher.Matches(entry, [new("", "1.0", name)]));
    }

    [Theory]
    [InlineData("Google.Chrome", true)]
    [InlineData("google.chrome", true)]
    [InlineData("Google.Chrome.Beta", false)]
    [InlineData("Other.Chrome", false)]
    public void ResolvedIdentitiesTakePriorityOverDisplayNames(string packageId, bool expected)
    {
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == "chrome");
        Assert.Equal(expected, InstalledSoftwareMatcher.Matches(entry, [new(packageId, "1.0", "Google Chrome")]));
    }

    [Theory]
    [InlineData("ARP\\Machine\\X86\\Google Chrome")]
    [InlineData("MSIX\\GoogleChrome_123")]
    [InlineData("{A1B2C3D4-0000-0000-0000-000000000000}")]
    public void UncorrelatedTableRowsReachDisplayNameMatching(string id)
    {
        static string Row(string name, string packageId, string version) =>
            name.PadRight(40) + packageId.PadRight(60) + version.PadRight(15) + "winget";
        var output = Row("Name", "Id", "Version") + "\n" + new string('-', 125) + "\n"
            + Row("Google Chrome", id, "154.0.8037.98");
        var packages = WingetService.ParseListTable(output);
        Assert.Single(packages);
        Assert.Empty(packages[0].PackageId);
        Assert.True(InstalledSoftwareMatcher.Matches(SoftwareCatalog.Entries.Single(e => e.Id == "chrome"), packages));
    }

    [Fact]
    public void CatalogRetainsCompanionsAndRemovesTheAnnotatedApp()
    {
        foreach (var id in new[] { "explorerpatcher", "openrgb", "fancontrol", "g-helper", "librehardwaremonitor", "signalrgb", "hwinfo" })
            Assert.Contains(SoftwareCatalog.Entries, e => e.Id == id);
        Assert.DoesNotContain(SoftwareCatalog.Entries, e => e.Id == "helium");
    }
}
