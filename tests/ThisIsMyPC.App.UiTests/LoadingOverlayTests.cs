using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.App.UiTests;

/// <summary>Slows winget down so the scan overlay is on screen long enough to photograph.</summary>
internal sealed class SlowWingetService : IWingetService
{
    private readonly UiFakeWingetService _inner = new();

    public Task<OperationResult<string>> GetVersionAsync(CancellationToken ct = default) =>
        _inner.GetVersionAsync(ct);

    public async Task<OperationResult<IReadOnlyList<InstalledWingetPackage>>> ListInstalledAsync(
        CancellationToken ct = default)
    {
        await Task.Delay(3000, ct);
        return await _inner.ListInstalledAsync(ct);
    }

    public Task<OperationResult<IReadOnlyList<UpgradableWingetPackage>>> ListUpgradableAsync(
        CancellationToken ct = default) => _inner.ListUpgradableAsync(ct);

    public Task<OperationResult<bool>> InstallAsync(string id, WingetSource s, CancellationToken ct = default) =>
        _inner.InstallAsync(id, s, ct);

    public Task<OperationResult<bool>> UpgradeAsync(string id, CancellationToken ct = default) =>
        _inner.UpgradeAsync(id, ct);

    public Task<OperationResult<bool>> UninstallAsync(string id, WingetSource s, CancellationToken ct = default) =>
        _inner.UninstallAsync(id, s, ct);
}

[Trait("Category", "Diagnostic")]
public class LoadingOverlayTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task NavigatingShowsTheScanOverlayThenTheContent()
    {
        using var session = UiSession.ForMainWindow("loading-overlay", services =>
        {
            services.RemoveAll<IWingetService>();
            services.AddSingleton<IWingetService, SlowWingetService>();
        });
        var viewModel = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(
            () => viewModel.SidebarGroups.Count > 0, timeoutMs: 30_000, what: "sidebar population");

        session.ClickText("Software");
        await session.WaitForAsync(() => viewModel.IsModuleLoading, timeoutMs: 10_000, what: "overlay showing");
        session.Screenshot("scan-overlay");
        Assert.True(session.IsTextVisible("Scanning Software..."));

        await session.WaitForAsync(
            () => viewModel.CurrentContent is SoftwareViewModel && !viewModel.IsModuleLoading,
            timeoutMs: 60_000, what: "software content load");
        session.Screenshot("software-loaded");
        Assert.True(session.IsTextVisible("App Catalog"));
    }
}
