using Windows.ApplicationModel.AppExtensions;

namespace ThisIsMyPC.Integration.Tests.Shell;

public class WinRtSpikeTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void AppExtensionCatalog_CanOpen_ContextMenuExtensions()
    {
        // V5 Spike: verify WinRT AppExtensionCatalog is accessible under the current TFM
        var catalog = AppExtensionCatalog.Open("windows.fileExplorerContextMenus");
        Assert.NotNull(catalog);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AppExtensionCatalog_FindAllAsync_ReturnsResults()
    {
        // V5 Spike: verify we can enumerate modern context menu extensions
        var catalog = AppExtensionCatalog.Open("windows.fileExplorerContextMenus");
        var extensions = await catalog.FindAllAsync();

        // On a Windows 11 system with modern apps, this should return entries
        // (Windows Terminal, PowerToys, etc.). May be empty on a bare system.
        Assert.NotNull(extensions);

        // Log what we found for diagnostic purposes
        foreach (var ext in extensions)
        {
            var pkg = ext.Package;
            System.Diagnostics.Debug.WriteLine(
                $"[WinRT Spike] Extension: {ext.DisplayName}, " +
                $"Package: {pkg.Id.FamilyName}, " +
                $"Publisher: {pkg.PublisherDisplayName}");
        }
    }
}
