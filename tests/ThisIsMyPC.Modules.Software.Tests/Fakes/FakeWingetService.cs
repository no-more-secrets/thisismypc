using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Modules.Software.Tests.Fakes;

public sealed class FakeWingetService : IWingetService
{
    public List<(string PackageId, WingetSource Source)> Installs { get; } = [];
    public List<(string PackageId, WingetSource Source)> Uninstalls { get; } = [];
    public List<InstalledWingetPackage> InstalledPackages { get; } = [];
    public List<UpgradableWingetPackage> UpgradablePackages { get; } = [];
    public List<string> Upgrades { get; } = [];
    public int UpgradableListCalls { get; private set; }

    public bool IsAvailable { get; set; } = true;
    public bool ListFails { get; set; }
    public bool UpgradableListFails { get; set; }
    public bool OperationsFail { get; set; }

    public Task<OperationResult<string>> GetVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsAvailable
            ? OperationResult<string>.Success("v1.9.0-fake")
            : OperationResult<string>.Failure("winget is not available.", ErrorCategory.ServiceUnavailable));

    public Task<OperationResult<IReadOnlyList<InstalledWingetPackage>>> ListInstalledAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ListFails
            ? OperationResult<IReadOnlyList<InstalledWingetPackage>>.Failure(
                "export failed", ErrorCategory.ServiceUnavailable)
            : OperationResult<IReadOnlyList<InstalledWingetPackage>>.Success(
                InstalledPackages.AsReadOnly()));

    public Task<OperationResult<IReadOnlyList<UpgradableWingetPackage>>> ListUpgradableAsync(
        CancellationToken cancellationToken = default)
    {
        UpgradableListCalls++;
        return Task.FromResult(UpgradableListFails
            ? OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Failure(
                "upgrade listing failed", ErrorCategory.ServiceUnavailable)
            : OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Success(
                UpgradablePackages.AsReadOnly()));
    }

    public Task<OperationResult<bool>> UpgradeAsync(
        string packageId, CancellationToken cancellationToken = default)
    {
        Upgrades.Add(packageId);
        return Task.FromResult(OperationsFail
            ? OperationResult<bool>.Failure("upgrade failed", ErrorCategory.ServiceUnavailable)
            : OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> InstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default)
    {
        Installs.Add((packageId, source));
        return Task.FromResult(OperationsFail
            ? OperationResult<bool>.Failure("install failed", ErrorCategory.ServiceUnavailable)
            : OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> UninstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default)
    {
        Uninstalls.Add((packageId, source));
        return Task.FromResult(OperationsFail
            ? OperationResult<bool>.Failure("uninstall failed", ErrorCategory.ServiceUnavailable)
            : OperationResult<bool>.Success(true));
    }
}
