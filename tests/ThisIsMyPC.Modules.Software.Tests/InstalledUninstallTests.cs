using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Interop.Win32.Packages;
using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Models;
using ThisIsMyPC.Modules.Software.Services;
using ThisIsMyPC.Modules.Software.Tests.Fakes;

namespace ThisIsMyPC.Modules.Software.Tests;

public sealed class InstalledUninstallTests
{
    [Theory]
    [InlineData("chatgpt", "9PLM9XGG6VKS", "ChatGPT")]
    [InlineData("zoom", "Zoom.Zoom", "Zoom Workplace")]
    [InlineData("chrome", "", "Google Chrome")]
    public async Task UninstallUsesInstalledIdentity_NotCatalogIdentity(string catalogId, string id, string name)
    {
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == catalogId);
        var winget = new FakeWingetService();
        var target = id.Length > 0 ? id : @"ARP\Machine\X64\Google Chrome";
        winget.InstalledPackages.Add(new(id, "1.0", name) { UninstallId = target });
        var module = new SoftwareModule(winget, new FakeAppxPackageService());
        var scan = Assert.IsType<SoftwareScanData>((await module.ScanSystemStateAsync()).Value);
        Assert.Contains(entry.WingetId, scan.InstalledWingetIds);
        Assert.DoesNotContain(entry.WingetId, scan.ExternallyManagedIds);
        Assert.True((await module.ExecuteActionAsync(SoftwareActionFactory.CreateUninstall(entry))).IsSuccess);
        Assert.Equal(target, Assert.Single(winget.Uninstalls).PackageId);
    }

    [Fact]
    public void ParserPreservesRawIdentity_ForExactUninstall()
    {
        var header = $"{"Name",-25}{"Id",-60}Version";
        var row = $"{"ChatGPT",-25}{@"MSIX\OpenAI.Codex_1.0_x64__publisher",-60}1.0";
        var package = Assert.Single(WingetService.ParseListTable(header + "\n" + new string('-', 95) + "\n" + row));
        Assert.Empty(package.PackageId);
        Assert.True(package.CanUninstall);
        var arguments = WingetService.BuildUninstallArguments(package).Value!;
        Assert.Equal(@"MSIX\OpenAI.Codex_1.0_x64__publisher", arguments[2]);
        Assert.Contains("--exact", arguments);
        Assert.Contains("--interactive", arguments);
        Assert.DoesNotContain("--source", arguments);
    }

    [Fact]
    public void TruncatedIdentityUsesCompleteNameAndVersion_WithNoShell()
    {
        var package = new InstalledWingetPackage("", "3.1", "Some App") { UninstallId = "ARP\\Truncated…" };
        var arguments = WingetService.BuildUninstallArguments(package).Value!;
        Assert.Equal(new[] { "uninstall", "--name", "Some App", "--version", "3.1" }, arguments.Take(5));
        Assert.False(WingetService.BuildUninstallArguments(package with { Name = "Truncated…" }).IsSuccess);
        Assert.False(WingetService.BuildUninstallArguments(package with { CanUninstall = false }).IsSuccess);
    }

    [Fact]
    public async Task MissingInstallationCannotRunCatalogUninstall()
    {
        var winget = new FakeWingetService();
        var module = new SoftwareModule(winget, new FakeAppxPackageService());
        var entry = SoftwareCatalog.Entries.Single(e => e.Id == "cursor");
        Assert.False((await module.ExecuteActionAsync(SoftwareActionFactory.CreateUninstall(entry))).IsSuccess);
        Assert.Empty(winget.Uninstalls);
    }

    [Fact]
    public async Task PortableFanControlUsesSharedHardwareDetection()
    {
        var winget = new FakeWingetService();
        var module = new SoftwareModule(winget, new FakeAppxPackageService(), new FanFacts());
        var scan = Assert.IsType<SoftwareScanData>((await module.ScanSystemStateAsync()).Value);
        Assert.Contains("Rem0o.FanControl", scan.InstalledWingetIds);
        Assert.Contains("Rem0o.FanControl", scan.ExternallyManagedIds);
        winget.InstalledPackages.Add(new("Rem0o.FanControl", "226"));
        scan = Assert.IsType<SoftwareScanData>((await module.ScanSystemStateAsync()).Value);
        Assert.DoesNotContain("Rem0o.FanControl", scan.ExternallyManagedIds);
    }

    private sealed class FanFacts : IHardwareFactsProvider
    {
        public HardwareDetectionSnapshot? Current { get; } = new(ObservedHardwareFacts.Empty, new HardwareSnapshot(),
            new Dictionary<CompanionApp, string> { [CompanionApp.FanControl] = @"C:\Apps\FanControl.exe" }, [], DateTimeOffset.Now);
        public event EventHandler? Changed { add { } remove { } }
        public Task<HardwareDetectionSnapshot> GetAsync(bool refresh = false, CancellationToken cancellationToken = default) => Task.FromResult(Current!);
    }
}
