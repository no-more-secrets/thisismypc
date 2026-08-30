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

    private static SoftwareModule CreateModule(
        FakeWingetService? winget = null, FakeAppxPackageService? appx = null) =>
        new(winget ?? new FakeWingetService(), appx ?? new FakeAppxPackageService());

    private static AppxPackageInfo CreatePackage(
        string packageId, bool? provisioned = false, bool isFramework = false) =>
        new(
            PackageFullName: $"{packageId}_1.0.0.0_x64__8wekyb3d8bbwe",
            PackageFamilyName: $"{packageId}_8wekyb3d8bbwe",
            DisplayName: packageId,
            PublisherDisplayName: "Microsoft",
            Version: "1.0.0.0",
            IsFramework: isFramework,
            SignatureKind: AppxSignatureKind.Store,
            IsProvisioned: provisioned);

    [Fact]
    public async Task CheckAvailability_UnavailableWhenWingetMissing()
    {
        var module = CreateModule(new FakeWingetService { IsAvailable = false });

        var availability = await module.CheckAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.NotNull(availability.RemediationHint);
    }

    [Fact]
    public async Task ScanSystemState_JoinsCatalogWithInstalledIds()
    {
        var fake = new FakeWingetService();
        fake.InstalledPackages.Add(new InstalledWingetPackage(FirstEntry.WingetId, "1.0"));
        var module = CreateModule(fake);

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
        var module = CreateModule(new FakeWingetService { ListFails = true });

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
        var module = CreateModule(fake);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateInstall(FirstEntry));

        Assert.True(result.IsSuccess);
        Assert.Equal([(FirstEntry.WingetId, FirstEntry.Source)], fake.Installs);
        Assert.Empty(fake.Uninstalls);
    }

    [Fact]
    public async Task ExecuteAction_UninstallRoutesToWinget()
    {
        var fake = new FakeWingetService();
        var module = CreateModule(fake);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateUninstall(FirstEntry));

        Assert.True(result.IsSuccess);
        Assert.Equal([(FirstEntry.WingetId, FirstEntry.Source)], fake.Uninstalls);
        Assert.Empty(fake.Installs);
    }

    [Fact]
    public async Task ExecuteAction_UnknownActionIdFails()
    {
        var module = CreateModule();

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
        var module = CreateModule();

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
        var module = CreateModule();

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

    [Fact]
    public void WindowsAppsCatalog_LoadsEmbeddedList()
    {
        var entries = WindowsAppsCatalog.Entries;

        Assert.True(entries.Count >= 30, $"Expected the ported winutil appx list, got {entries.Count}");
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
        Assert.All(entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.PackageId));
            Assert.DoesNotContain(e.PackageId, c => char.IsWhiteSpace(c));
        });
        // Most entries carry a Store id for the reinstall path.
        Assert.True(entries.Count(e => e.CanReinstall) >= 25);
    }

    [Fact]
    public async Task ScanSystemState_MarksPresentWindowsApps()
    {
        var target = WindowsAppsCatalog.Entries[0];
        var appx = new FakeAppxPackageService();
        appx.Packages.Add(CreatePackage(target.PackageId));
        var module = CreateModule(appx: appx);

        var result = await module.ScanSystemStateAsync();

        var scan = Assert.IsType<SoftwareScanData>(result.Value);
        Assert.True(scan.AppxStateKnown);
        Assert.Contains(target.PackageId, scan.PresentAppxPackageIds);
        Assert.Equal(WindowsAppsCatalog.Entries.Count, scan.WindowsApps.Count);
    }

    [Fact]
    public async Task ScanSystemState_AppxEnumerationFailureIsNonFatal()
    {
        var module = CreateModule(appx: new FakeAppxPackageService { EnumerateFails = true });

        var result = await module.ScanSystemStateAsync();

        var scan = Assert.IsType<SoftwareScanData>(result.Value);
        Assert.False(scan.AppxStateKnown);
        Assert.Empty(scan.PresentAppxPackageIds);
    }

    [Fact]
    public async Task ExecuteAction_AppxRemoveRemovesAllUsersAndDeprovisionsWhenProvisioned()
    {
        var target = WindowsAppsCatalog.Entries[0];
        var appx = new FakeAppxPackageService();
        appx.Packages.Add(CreatePackage(target.PackageId, provisioned: true));
        appx.Packages.Add(CreatePackage("Unrelated.App"));
        var module = CreateModule(appx: appx);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxRemove(target));

        Assert.True(result.IsSuccess);
        var removal = Assert.Single(appx.Removals);
        Assert.StartsWith(target.PackageId + "_", removal.PackageFullName, StringComparison.Ordinal);
        Assert.True(removal.AllUsers);
        Assert.Equal([target.PackageId + "_8wekyb3d8bbwe"], appx.Deprovisions);
    }

    [Fact]
    public async Task ExecuteAction_AppxRemoveSkipsDeprovisionWhenNotProvisioned()
    {
        var target = WindowsAppsCatalog.Entries[0];
        var appx = new FakeAppxPackageService();
        appx.Packages.Add(CreatePackage(target.PackageId, provisioned: false));
        var module = CreateModule(appx: appx);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxRemove(target));

        Assert.True(result.IsSuccess);
        Assert.Single(appx.Removals);
        Assert.Empty(appx.Deprovisions);
    }

    [Fact]
    public async Task ExecuteAction_AppxRemoveAttemptsDeprovisionWhenProvisionedStateUnknown()
    {
        var target = WindowsAppsCatalog.Entries[0];
        var appx = new FakeAppxPackageService();
        appx.Packages.Add(CreatePackage(target.PackageId, provisioned: null));
        var module = CreateModule(appx: appx);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxRemove(target));

        Assert.True(result.IsSuccess);
        Assert.Single(appx.Deprovisions);
    }

    [Fact]
    public async Task ExecuteAction_AppxRemoveIgnoresFrameworkPackages()
    {
        var target = WindowsAppsCatalog.Entries[0];
        var appx = new FakeAppxPackageService();
        appx.Packages.Add(CreatePackage(target.PackageId, isFramework: true));
        var module = CreateModule(appx: appx);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxRemove(target));

        Assert.True(result.IsSuccess);
        Assert.Empty(appx.Removals);
    }

    [Fact]
    public async Task ExecuteAction_AppxRemoveAlreadyAbsentSucceeds()
    {
        var target = WindowsAppsCatalog.Entries[0];
        var module = CreateModule(appx: new FakeAppxPackageService());

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxRemove(target));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAction_AppxRemoveSurfacesRemovalFailure()
    {
        var target = WindowsAppsCatalog.Entries[0];
        var appx = new FakeAppxPackageService { RemoveFails = true };
        appx.Packages.Add(CreatePackage(target.PackageId));
        var module = CreateModule(appx: appx);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxRemove(target));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAction_AppxReinstallUsesStoreSource()
    {
        var target = WindowsAppsCatalog.Entries.First(e => e.CanReinstall);
        var winget = new FakeWingetService();
        var module = CreateModule(winget);

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxReinstall(target));

        Assert.True(result.IsSuccess);
        Assert.Equal([(target.StoreId, WingetSource.MsStore)], winget.Installs);
    }

    [Fact]
    public async Task ExecuteAction_AppxReinstallWithoutStoreIdFails()
    {
        var target = WindowsAppsCatalog.Entries.FirstOrDefault(e => !e.CanReinstall);
        Assert.NotNull(target); // three winutil entries ship without Store ids

        var module = CreateModule();

        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateAppxReinstall(target));

        Assert.False(result.IsSuccess);
    }
}
