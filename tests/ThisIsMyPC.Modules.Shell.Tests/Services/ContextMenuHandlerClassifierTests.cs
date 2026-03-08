using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ContextMenuHandlerClassifierTests
{
    [Theory]
    [InlineData("{09799AFB-AD67-11d1-ABCD-00C04FC30936}")] // Open With
    [InlineData("{7BA4C740-9E81-11CF-99D3-00AA004AE837}")] // SendTo
    [InlineData("{D969A300-E7FF-11d0-A93B-00A0C90F2719}")] // New Menu
    [InlineData("{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}")] // Copy as Path
    [InlineData("{00021401-0000-0000-C000-000000000046}")] // Shortcut (.lnk)
    [InlineData("{85cfccaf-2d14-42b6-80b6-f40f65d016e7}")] // Shortcut (.symlink)
    [InlineData("{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}")] // Sharing
    [InlineData("{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}")] // Start Menu Pin
    [InlineData("{90AA3A4E-1CBA-4233-B8BB-535773D48449}")] // Taskband Pin
    [InlineData("{A470F8CF-A1E8-4f65-8335-227475AA5C46}")] // Encryption
    public void Classify_known_critical_CLSID_returns_Critical(string clsid)
    {
        var result = ContextMenuHandlerClassifier.Classify(clsid, @"C:\Windows\System32\shell32.dll");

        Assert.Equal(HandlerClassification.Critical, result);
    }

    [Fact]
    public void Classify_critical_CLSID_is_case_insensitive()
    {
        var result = ContextMenuHandlerClassifier.Classify(
            "{09799afb-ad67-11d1-abcd-00c04fc30936}",
            @"C:\Windows\System32\shell32.dll");

        Assert.Equal(HandlerClassification.Critical, result);
    }

    [Fact]
    public void Classify_Microsoft_system_DLL_returns_System()
    {
        // shell32.dll in System32 with a non-critical CLSID
        var result = ContextMenuHandlerClassifier.Classify(
            "{11111111-1111-1111-1111-111111111111}",
            @"C:\Windows\System32\shell32.dll");

        Assert.Equal(HandlerClassification.System, result);
    }

    [Fact]
    public void Classify_PowerToys_DLL_with_publisher_returns_Optional()
    {
        var result = ContextMenuHandlerClassifier.Classify(
            "{84D68575-E186-46AD-B0CB-BAEB45EE29C0}",
            @"C:\Program Files\PowerToys\modules\FileLocksmith\PowerToys.FileLocksmithExt.dll",
            publisher: "Microsoft Corporation");

        Assert.Equal(HandlerClassification.Optional, result);
    }

    [Fact]
    public void Classify_PowerToys_path_without_version_info_returns_Optional()
    {
        // Even without publisher info, PowerToys path should classify as Optional
        var result = ContextMenuHandlerClassifier.Classify(
            "{84D68575-E186-46AD-B0CB-BAEB45EE29C0}",
            @"C:\Program Files\PowerToys\modules\FileLocksmith\PowerToys.FileLocksmithExt.dll");

        Assert.Equal(HandlerClassification.Optional, result);
    }

    [Fact]
    public void Classify_third_party_DLL_returns_ThirdParty()
    {
        var result = ContextMenuHandlerClassifier.Classify(
            "{23170F69-40C1-278A-1000-000100020000}",
            @"C:\Program Files\7-Zip\7-zip.dll",
            publisher: "Igor Pavlov");

        Assert.Equal(HandlerClassification.ThirdParty, result);
    }

    [Fact]
    public void Classify_null_DLL_path_returns_ThirdParty()
    {
        var result = ContextMenuHandlerClassifier.Classify(
            "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            null);

        Assert.Equal(HandlerClassification.ThirdParty, result);
    }

    [Fact]
    public void Classify_empty_DLL_path_returns_ThirdParty()
    {
        var result = ContextMenuHandlerClassifier.Classify(
            "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            string.Empty);

        Assert.Equal(HandlerClassification.ThirdParty, result);
    }

    [Fact]
    public void Classify_nonexistent_DLL_path_returns_ThirdParty()
    {
        var result = ContextMenuHandlerClassifier.Classify(
            "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            @"C:\NonExistent\Path\fake.dll");

        Assert.Equal(HandlerClassification.ThirdParty, result);
    }

    [Fact]
    public void Classify_Microsoft_publisher_non_PowerToys_returns_System()
    {
        var result = ContextMenuHandlerClassifier.Classify(
            "{11111111-1111-1111-1111-111111111111}",
            @"C:\Windows\System32\some.dll",
            publisher: "Microsoft Corporation");

        Assert.Equal(HandlerClassification.System, result);
    }

    [Fact]
    public void Classify_critical_CLSID_takes_precedence_over_DLL_path()
    {
        // Even with a null DLL path, critical CLSID should return Critical
        var result = ContextMenuHandlerClassifier.Classify(
            "{09799AFB-AD67-11d1-ABCD-00C04FC30936}",
            null);

        Assert.Equal(HandlerClassification.Critical, result);
    }
}
