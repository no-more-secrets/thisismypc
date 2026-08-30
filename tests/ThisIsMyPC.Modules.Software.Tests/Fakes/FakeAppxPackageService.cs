using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Modules.Software.Tests.Fakes;

public sealed class FakeAppxPackageService : IAppxPackageService
{
    public List<AppxPackageInfo> Packages { get; } = [];
    public List<(string PackageFullName, bool AllUsers)> Removals { get; } = [];
    public List<string> Deprovisions { get; } = [];

    public bool EnumerateFails { get; set; }
    public bool RemoveFails { get; set; }

    public Task<OperationResult<IReadOnlyList<AppxPackageInfo>>> EnumeratePackagesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(EnumerateFails
            ? OperationResult<IReadOnlyList<AppxPackageInfo>>.Failure(
                "enumerate failed", ErrorCategory.ServiceUnavailable)
            : OperationResult<IReadOnlyList<AppxPackageInfo>>.Success(Packages.AsReadOnly()));

    public Task<OperationResult<AppxPackageInfo>> QueryPackageAsync(
        string packageFullName, CancellationToken cancellationToken = default)
    {
        var package = Packages.FirstOrDefault(p => p.PackageFullName == packageFullName);
        return Task.FromResult(package is not null
            ? OperationResult<AppxPackageInfo>.Success(package)
            : OperationResult<AppxPackageInfo>.Failure("not found", ErrorCategory.NotFound));
    }

    public Task<OperationResult<bool>> RemovePackageAsync(
        string packageFullName, bool allUsers = false, CancellationToken cancellationToken = default)
    {
        Removals.Add((packageFullName, allUsers));
        return Task.FromResult(RemoveFails
            ? OperationResult<bool>.Failure("remove failed", ErrorCategory.AccessDenied)
            : OperationResult<bool>.Success(true));
    }

    public Task<OperationResult<bool>> DeprovisionPackageAsync(
        string packageFamilyName, CancellationToken cancellationToken = default)
    {
        Deprovisions.Add(packageFamilyName);
        return Task.FromResult(OperationResult<bool>.Success(true));
    }
}
