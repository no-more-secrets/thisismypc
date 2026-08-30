using ThisIsMyPC.Interop.Win32.Packages;

namespace ThisIsMyPC.Modules.Software.Tests;

/// <summary>
/// ParseUpgradeTable reads the fixed-width table of "winget upgrade" by taking
/// the last four whitespace tokens of each data row, so it must survive
/// localized headers, multi-word names, truncation ellipses, progress garbage
/// and the second "explicit targeting" section.
/// </summary>
public sealed class WingetUpgradeParsingTests
{
    [Fact]
    public void ParsesTypicalTable()
    {
        const string output = """
            Name                     Id                          Version   Available  Source
            -----------------------------------------------------------------------------------
            Mozilla Firefox          Mozilla.Firefox             133.0     134.0.1    winget
            Git                      Git.Git                     2.47.0    2.48.1     winget
            Microsoft Visual Studio  Microsoft.VisualStudio.2026 17.0.1    17.0.2     winget
            3 upgrades available.
            """;

        var packages = WingetService.ParseUpgradeTable(output);

        Assert.Equal(3, packages.Count);
        Assert.Equal("Mozilla.Firefox", packages[0].PackageId);
        Assert.Equal("Mozilla Firefox", packages[0].Name);
        Assert.Equal("133.0", packages[0].InstalledVersion);
        Assert.Equal("134.0.1", packages[0].AvailableVersion);
        Assert.Equal("Microsoft Visual Studio", packages[2].Name);
    }

    [Fact]
    public void LocalizedHeadersAreIrrelevant()
    {
        const string output = """
            Name                Id                 Version  Verfügbar  Quelle
            ------------------------------------------------------------------
            Mozilla Firefox     Mozilla.Firefox    133.0    134.0.1    winget
            1 Aktualisierung verfügbar.
            """;

        var packages = WingetService.ParseUpgradeTable(output);

        Assert.Single(packages);
        Assert.Equal("Mozilla.Firefox", packages[0].PackageId);
    }

    [Fact]
    public void SkipsRowsWithTruncatedIds()
    {
        const string output = """
            Name             Id                              Version  Available  Source
            ---------------------------------------------------------------------------
            Some Long App    Publisher.SomeVeryLongPackage…  1.0      2.0        winget
            Git              Git.Git                         2.47.0   2.48.1     winget
            """;

        var packages = WingetService.ParseUpgradeTable(output);

        Assert.Single(packages);
        Assert.Equal("Git.Git", packages[0].PackageId);
    }

    [Fact]
    public void ParsesExplicitTargetingSection()
    {
        const string output = """
            Name             Id               Version  Available  Source
            --------------------------------------------------------------
            Git              Git.Git          2.47.0   2.48.1     winget
            1 upgrade available.

            The following packages have an upgrade available, but require explicit targeting for upgrade:
            Name             Id               Version  Available  Source
            --------------------------------------------------------------
            Some Pinned App  Vendor.Pinned    1.0.0    1.1.0      winget
            """;

        var packages = WingetService.ParseUpgradeTable(output);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, p => p.PackageId == "Vendor.Pinned");
    }

    [Fact]
    public void IgnoresProgressGarbageAndBlankLines()
    {
        // Redirected winget output carries spinner backspaces and in-place
        // carriage-return rewrites ahead of the table.
        const string output =
            "-\b\\\b|\b/\b-\b \r" +
            "   \r\n" +
            "Name   Id        Version  Available  Source\n" +
            "--------------------------------------------\n" +
            "Git    Git.Git   2.47.0   2.48.1     winget\n";

        var packages = WingetService.ParseUpgradeTable(output);

        Assert.Single(packages);
        Assert.Equal("Git.Git", packages[0].PackageId);
    }

    [Fact]
    public void VersionsContainingSpacesParseByColumnOffsets()
    {
        // Real winget output: "< x.y" range markers and "(build)" suffixes put
        // spaces inside the version columns, so token counting cannot work.
        const string output = """
            Name             Id               Version              Available           Source
            ---------------------------------------------------------------------------------
            Unity Hub 3.18.0 Unity.UnityHub   < 3.21.0.65535       3.21.0.65535        winget
            Zoom Workplace   Zoom.Zoom.EXE    6.6.11 (23272)       7.1.5 (43453)       winget
            """;

        var packages = WingetService.ParseUpgradeTable(output);

        Assert.Equal(2, packages.Count);
        Assert.Equal("Unity.UnityHub", packages[0].PackageId);
        Assert.Equal("< 3.21.0.65535", packages[0].InstalledVersion);
        Assert.Equal("3.21.0.65535", packages[0].AvailableVersion);
        Assert.Equal("Zoom.Zoom.EXE", packages[1].PackageId);
        Assert.Equal("6.6.11 (23272)", packages[1].InstalledVersion);
        Assert.Equal("7.1.5 (43453)", packages[1].AvailableVersion);
    }

    [Fact]
    public void DuplicateIdsAcrossSectionsCollapse()
    {
        const string output = """
            Name   Id        Version  Available  Source
            --------------------------------------------
            Git    Git.Git   2.47.0   2.48.1     winget
            Git    Git.Git   2.47.0   2.48.1     winget
            """;

        var packages = WingetService.ParseUpgradeTable(output);

        Assert.Single(packages);
    }

    [Fact]
    public void EmptyOutputYieldsNoPackages()
    {
        Assert.Empty(WingetService.ParseUpgradeTable(string.Empty));
        Assert.Empty(WingetService.ParseUpgradeTable("No installed package found matching input criteria."));
    }

    // ---- winget list (installed detection) ----

    [Fact]
    public void ListTable_ParsesRowsAndSkipsArpJunk()
    {
        // Real winget list traits: empty Available cells, ARP identifiers with
        // spaces or braces, truncated ids, "Unknown" versions.
        const string output = """
            Name                Id                                     Version      Available  Source
            ------------------------------------------------------------------------------------------
            Google Chrome       Google.Chrome                          139.0.7258   140.0.7339 winget
            Node.js             OpenJS.NodeJS                          22.14.0                 winget
            Some Legacy Tool    {A1B2C3D4-0000-0000-0000-000000000000} 1.0
            Mystery App         Mystery App 2.5                        2.5
            Too Long Package    Publisher.SomeVeryLongPackageNameTru…  1.0                     winget
            Driver Bundle       Vendor.Driver                          Unknown                 winget
            """;

        var packages = WingetService.ParseListTable(output);

        // Unusable ids (spaces, truncation) survive as name-only rows so the
        // catalog can still match them by display name.
        Assert.Equal(["Google.Chrome", "OpenJS.NodeJS", "{A1B2C3D4-0000-0000-0000-000000000000}", "", "", "Vendor.Driver"],
            packages.Select(p => p.PackageId).ToArray());
        Assert.Equal("139.0.7258", packages[0].Version);
        Assert.Equal("Mystery App", packages[3].Name);
        Assert.Equal("Too Long Package", packages[4].Name);
        Assert.Equal("Unknown", packages[5].Version);
    }

    [Fact]
    public void ListTable_FourColumnVariantParses()
    {
        const string output = """
            Name             Id               Version  Source
            ----------------------------------------------------
            Git              Git.Git          2.52.0   winget
            """;

        var packages = WingetService.ParseListTable(output);

        Assert.Single(packages);
        Assert.Equal("Git.Git", packages[0].PackageId);
        Assert.Equal("2.52.0", packages[0].Version);
    }
}
