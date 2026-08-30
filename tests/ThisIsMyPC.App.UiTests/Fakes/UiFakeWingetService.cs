using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>Instant, deterministic winget: a few well-known apps read as installed.</summary>
public sealed class UiFakeWingetService : IWingetService
{
    public List<InstalledWingetPackage> Installed { get; } =
    [
        new("Git.Git", "2.47.0"),
        new("Microsoft.VisualStudioCode", "1.95.0"),
        new("Mozilla.Firefox", "133.0"),
    ];

    public List<UpgradableWingetPackage> Upgradable { get; } =
    [
        new("Mozilla.Firefox", "Mozilla Firefox", "133.0", "134.0.1"),
        new("Git.Git", "Git", "2.47.0", "2.48.1"),
    ];

    public List<(string PackageId, WingetSource Source)> Installs { get; } = [];
    public List<(string PackageId, WingetSource Source)> Uninstalls { get; } = [];
    public List<string> Upgrades { get; } = [];

    public Task<OperationResult<string>> GetVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<string>.Success("v1.9.0-uitest"));

    public Task<OperationResult<IReadOnlyList<InstalledWingetPackage>>> ListInstalledAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<InstalledWingetPackage>>.Success(
            Installed.AsReadOnly()));

    public Task<OperationResult<IReadOnlyList<UpgradableWingetPackage>>> ListUpgradableAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Success(
            Upgradable.AsReadOnly()));

    public Task<OperationResult<bool>> UpgradeAsync(
        string packageId, CancellationToken cancellationToken = default)
    {
        Upgrades.Add(packageId);
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> InstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default)
    {
        Installs.Add((packageId, source));
        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> UninstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default)
    {
        Uninstalls.Add((packageId, source));
        return Task.FromResult(OperationResult<bool>.Success(true));
    }
}
