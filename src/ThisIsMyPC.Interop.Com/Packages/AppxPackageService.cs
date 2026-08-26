using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace ThisIsMyPC.Interop.Com.Packages;

public sealed class AppxPackageService : IAppxPackageService
{
    public Task<OperationResult<IReadOnlyList<AppxPackageInfo>>> EnumeratePackagesAsync(
        CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try
            {
                var manager = new PackageManager();
                var provisioned = TryGetProvisionedFamilyNames(manager);

                var infos = new List<AppxPackageInfo>();
                foreach (var package in manager.FindPackagesForUser(string.Empty))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    infos.Add(ToInfo(package, provisioned));
                }
                return OperationResult<IReadOnlyList<AppxPackageInfo>>.Success(infos);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var (category, message) = AppxErrorMapper.Map(ex.HResult, "installed packages", "enumerate");
                return OperationResult<IReadOnlyList<AppxPackageInfo>>.Failure(message, category, ex);
            }
        }, cancellationToken);

    public Task<OperationResult<AppxPackageInfo>> QueryPackageAsync(
        string packageFullName, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try
            {
                var manager = new PackageManager();
                var package = manager.FindPackageForUser(string.Empty, packageFullName);
                if (package is null)
                    return OperationResult<AppxPackageInfo>.Failure(
                        $"Cannot query package '{packageFullName}': no such package is installed for the current user.",
                        ErrorCategory.NotFound);

                var provisioned = TryGetProvisionedFamilyNames(manager);
                return OperationResult<AppxPackageInfo>.Success(ToInfo(package, provisioned));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var (category, message) = AppxErrorMapper.Map(ex.HResult, $"package '{packageFullName}'", "query");
                return OperationResult<AppxPackageInfo>.Failure(message, category, ex);
            }
        }, cancellationToken);

    public async Task<OperationResult<bool>> RemovePackageAsync(
        string packageFullName, bool allUsers = false, CancellationToken cancellationToken = default)
    {
        try
        {
            // Cancellation is honored only before the deployment starts: cancelling the
            // engine mid-removal would report OCE with the final package state unknown.
            cancellationToken.ThrowIfCancellationRequested();
            var manager = new PackageManager();
            var operation = allUsers
                ? manager.RemovePackageAsync(packageFullName, RemovalOptions.RemoveForAllUsers)
                : manager.RemovePackageAsync(packageFullName);
            var result = await operation.AsTask(CancellationToken.None).ConfigureAwait(false);
            return CompleteDeployment(result, $"package '{packageFullName}'", "remove");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var (category, message) = AppxErrorMapper.Map(ex.HResult, $"package '{packageFullName}'", "remove");
            return OperationResult<bool>.Failure(AppendErrorText(message, ex), category, ex);
        }
    }

    public async Task<OperationResult<bool>> DeprovisionPackageAsync(
        string packageFamilyName, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manager = new PackageManager();
            var result = await manager.DeprovisionPackageForAllUsersAsync(packageFamilyName)
                .AsTask(CancellationToken.None).ConfigureAwait(false);
            return CompleteDeployment(result, $"package family '{packageFamilyName}'", "deprovision");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var (category, message) = AppxErrorMapper.Map(ex.HResult, $"package family '{packageFamilyName}'", "deprovision");
            return OperationResult<bool>.Failure(AppendErrorText(message, ex), category, ex);
        }
    }

    private static OperationResult<bool> CompleteDeployment(
        DeploymentResult result, string subject, string verb)
    {
        // Deployment failures normally throw from the await; a non-null ExtendedErrorCode
        // on a returned result is the belt-and-braces path.
        if (result.ExtendedErrorCode is null)
            return OperationResult<bool>.Success(true);

        var (category, message) = AppxErrorMapper.Map(result.ExtendedErrorCode.HResult, subject, verb);
        if (!string.IsNullOrWhiteSpace(result.ErrorText))
            message = $"{message} {result.ErrorText}";
        return OperationResult<bool>.Failure(message, category);
    }

    private static string AppendErrorText(string message, Exception ex)
        => ex is COMException && !string.IsNullOrWhiteSpace(ex.Message)
            ? $"{message} {ex.Message}"
            : message;

    private static AppxPackageInfo ToInfo(Package package, IReadOnlySet<string>? provisionedFamilyNames)
    {
        var id = package.Id;
        var version = id.Version;
        return new AppxPackageInfo(
            PackageFullName: id.FullName,
            PackageFamilyName: id.FamilyName,
            DisplayName: SafeDisplayString(() => package.DisplayName, id.Name),
            PublisherDisplayName: SafeDisplayString(() => package.PublisherDisplayName, id.Publisher),
            Version: $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
            IsFramework: package.IsFramework,
            SignatureKind: package.SignatureKind switch
            {
                PackageSignatureKind.Developer => AppxSignatureKind.Developer,
                PackageSignatureKind.Enterprise => AppxSignatureKind.Enterprise,
                PackageSignatureKind.Store => AppxSignatureKind.Store,
                PackageSignatureKind.System => AppxSignatureKind.System,
                _ => AppxSignatureKind.None,
            },
            IsProvisioned: provisionedFamilyNames?.Contains(id.FamilyName));
    }

    // Resource-backed display names throw for some system/staged packages when the
    // resource cannot be resolved — fall back to the raw identity string.
    private static string SafeDisplayString(Func<string> read, string fallback)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    // Reading the provisioned list requires elevation; enumeration must still work
    // without it, so a failure here degrades to "unknown" rather than failing the call.
    private static IReadOnlySet<string>? TryGetProvisionedFamilyNames(PackageManager manager)
    {
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in manager.FindProvisionedPackages())
                names.Add(package.Id.FamilyName);
            return names;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
