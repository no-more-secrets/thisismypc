using System.Runtime.InteropServices;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Loads the complete packaged native dependency set before CIG closes image
/// loading. The release directory is admin-only, and every file must carry the
/// same trusted publisher as the application.
/// </summary>
internal static class TrustedNativeDependencyLoader
{
    internal static IReadOnlyList<TrustedNativeDependency> ExpectedDependencies { get; } =
    [
        new("av_libglesv2.dll", "9B203E40323B49DAD29546A52B8B67D200BBA8FF4CAB9709A79CEDE23BA847D4"),
        new("e_sqlite3.dll", "4B615E0717F7E12EDC1CF9100AF77130562421423C25E34F24E80FD982F3DE72"),
        new("libHarfBuzzSharp.dll", "874FA400D53CEDAD95390743A02510C1CBED2EC659378805CC5FA4192CD87059"),
        new("libSkiaSharp.dll", "38ED0F8307E1CCC84A52FFB2216C59A5660721173C81F524249756CF76DBBF54"),
    ];

    internal static string[] FileNames => [.. ExpectedDependencies.Select(static dependency => dependency.FileName)];

    private static readonly List<nint> Handles = [];

    internal static OperationResult<bool> VerifyAndLoad(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var loadedHandles = new List<nint>(ExpectedDependencies.Count);
        try
        {
            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in ExpectedDependencies)
            {
                var path = Path.GetFullPath(Path.Combine(directory, dependency.FileName));
                expectedPaths.Add(path);

                string actualHash;
                try
                {
                    actualHash = AuthenticodeCanonicalHash.ComputeSha256(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    return OperationResult<bool>.Failure(
                        $"Canonical hash verification failed for {dependency.FileName}: {ex.Message}",
                        ErrorCategory.AccessDenied,
                        ex);
                }

                if (!string.Equals(actualHash, dependency.CanonicalSha256, StringComparison.Ordinal))
                {
                    return OperationResult<bool>.Failure(
                        $"The native dependency does not match its baked SHA-256: {dependency.FileName}.",
                        ErrorCategory.AccessDenied);
                }

                var trust = AuthenticodeVerifier.VerifyTrusted(
                    path,
                    AppConstants.PublisherName,
                    exactSignerName: true);
                if (!trust.IsSuccess)
                {
                    return OperationResult<bool>.Failure(
                        trust.ErrorMessage ?? $"{dependency.FileName} failed signature verification.",
                        ErrorCategory.AccessDenied,
                        trust.Exception);
                }

                try
                {
                    var handle = NativeLibrary.Load(path);
                    loadedHandles.Add(handle);
                    var loadedPath = Path.GetFullPath(ProcessModuleInventory.GetModulePath(handle));
                    if (!string.Equals(path, loadedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return OperationResult<bool>.Failure(
                            $"The Windows loader mapped {dependency.FileName} from an unexpected path: {loadedPath}.",
                            ErrorCategory.AccessDenied);
                    }
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or IOException)
                {
                    return OperationResult<bool>.Failure(
                        $"The trusted native dependency could not load: {dependency.FileName}.",
                        ErrorCategory.ServiceUnavailable,
                        ex);
                }
            }

            try
            {
                var inventory = ValidateLoadedModules(
                    ProcessModuleInventory.GetCurrentModulePaths(),
                    Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unavailable."),
                    directory,
                    expectedPaths);
                if (!inventory.IsSuccess)
                    return inventory;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                return OperationResult<bool>.Failure(
                    $"The pre-CIG module inventory failed: {ex.Message}",
                    ErrorCategory.AccessDenied,
                    ex);
            }

            Handles.AddRange(loadedHandles);
            loadedHandles.Clear();
            return OperationResult<bool>.Success(true);
        }
        finally
        {
            foreach (var handle in loadedHandles)
                NativeLibrary.Free(handle);
        }
    }

    internal static OperationResult<bool> ValidateLoadedModules(
        IReadOnlyList<string> loadedModulePaths,
        string processPath,
        string directory,
        IReadOnlySet<string>? expectedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(loadedModulePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var applicationPath = Path.GetFullPath(processPath);
        var applicationDirectory = Path.GetFullPath(directory);
        var expected = expectedPaths ?? ExpectedDependencies
            .Select(dependency => Path.GetFullPath(Path.Combine(applicationDirectory, dependency.FileName)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var systemDirectory = Path.GetFullPath(Environment.SystemDirectory);
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var componentStore = Path.GetFullPath(Path.Combine(windowsDirectory, "WinSxS"));
        var observedExpected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modulePath in loadedModulePaths)
        {
            var fullPath = Path.GetFullPath(modulePath);
            if (string.Equals(fullPath, applicationPath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (expected.Contains(fullPath))
            {
                observedExpected.Add(fullPath);
                continue;
            }
            if (IsUnder(fullPath, systemDirectory) || IsUnder(fullPath, componentStore))
                continue;

            return OperationResult<bool>.Failure(
                $"An unexpected non-system module loaded before CIG: {fullPath}.",
                ErrorCategory.AccessDenied);
        }

        var missing = expected.FirstOrDefault(path => !observedExpected.Contains(path));
        return missing is null
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(
                $"An expected native dependency is absent from the module inventory: {Path.GetFileName(missing)}.",
                ErrorCategory.AccessDenied);
    }

    private static bool IsUnder(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(directory);
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct TrustedNativeDependency(string FileName, string CanonicalSha256);
