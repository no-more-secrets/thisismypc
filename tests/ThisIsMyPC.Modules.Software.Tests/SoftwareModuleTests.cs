using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;
using ThisIsMyPC.Modules.Software.Tests.Fakes;

namespace ThisIsMyPC.Modules.Software.Tests;

public class SoftwareModuleTests
{
    private static SoftwareCatalogEntry FirstEntry => SoftwareCatalog.Entries[0];

    [Fact]
    public async Task CheckAvailability_UnavailableWhenWingetMissing()
    {
        var module = new SoftwareModule(new FakeWingetService { IsAvailable = false });

        var availability = await module.CheckAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.NotNull(availability.RemediationHint);
    }

    [Fact]
    public async Task ScanSystemState_JoinsCatalogWithInstalledIds()
    {
        var fake = new FakeWingetService();
        fake.InstalledPackages.Add(new InstalledWingetPackage(FirstEntry.WingetId, "1.0"));
        var module = new SoftwareModule(fake);

        var result = await module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var scan = Assert.IsType<SoftwareScanData>(result.Value);
        Assert.True(scan.InstalledStateKnown);
        Assert.Contains(FirstEntry.WingetId, scan.InstalledWingetIds);
        Assert.Equal(SoftwareCatalog.Entries.Count, scan.Catalog.Count);
    }

    [Fact]
    public async Task ScanSystemState_ExportFailureLeavesCatalogBrowsable()
    {
        var module = new SoftwareModule(new FakeWingetService { ListFails = true });

        var result = await module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var scan = Assert.IsType<SoftwareScanData>(result.Value);
        Assert.False(scan.InstalledStateKnown);
        Assert.Empty(scan.InstalledWingetIds);
        Assert.NotEmpty(scan.Catalog);
    }

    [Fact]
    public async Task ExecuteAction_InstallRoutesToWingetWithCatalogSource()
    {
        var fake = new FakeWingetService();
        var module = new SoftwareModule(fake);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateInstall(FirstEntry));

        Assert.True(result.IsSuccess);
        Assert.Equal([(FirstEntry.WingetId, FirstEntry.Source)], fake.Installs);
        Assert.Empty(fake.Uninstalls);
    }

    [Fact]
    public async Task ExecuteAction_UninstallRoutesToWinget()
    {
        var fake = new FakeWingetService();
        var module = new SoftwareModule(fake);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateUninstall(FirstEntry));

        Assert.True(result.IsSuccess);
        Assert.Equal([(FirstEntry.WingetId, FirstEntry.Source)], fake.Uninstalls);
        Assert.Empty(fake.Installs);
    }

    [Fact]
    public async Task ExecuteAction_UnknownActionIdFails()
    {
        var module = new SoftwareModule(new FakeWingetService());

        var result = await module.ExecuteActionAsync(new ActionDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            ActionId = "frobnicate:everything",
            DisplayName = "Frobnicate",
            Detail = "n/a",
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAction_UnknownCatalogIdFails()
    {
        var module = new SoftwareModule(new FakeWingetService());

        var result = await module.ExecuteActionAsync(new ActionDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            ActionId = "install:not-a-real-app",
            DisplayName = "Install nothing",
            Detail = "n/a",
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyChange_AlwaysFails()
    {
        var module = new SoftwareModule(new FakeWingetService());

        var result = await module.ApplyChangeAsync(new Core.Changes.ChangeDescriptor
        {
            ModuleId = SoftwareModule.ModuleName,
            SettingId = "x",
            DisplayName = "x",
            SystemLocation = "x",
            BeforeValue = "0",
            AfterValue = "1",
            BeforeDisplay = "0",
            AfterDisplay = "1",
            ValueType = Core.Changes.ChangeValueType.Registry_DWord,
        });

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ActionFactory_ProducesQueueReadyDescriptors()
    {
        var install = SoftwareActionFactory.CreateInstall(FirstEntry);
        var uninstall = SoftwareActionFactory.CreateUninstall(FirstEntry);

        Assert.Equal(SoftwareModule.ModuleName, install.ModuleId);
        Assert.StartsWith("install:", install.ActionId, StringComparison.Ordinal);
        Assert.StartsWith("uninstall:", uninstall.ActionId, StringComparison.Ordinal);
        Assert.NotEqual(install.ActionId, uninstall.ActionId);
        Assert.NotNull(install.UndoHint);
        Assert.NotNull(uninstall.UndoHint);
    }
}
