using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Security;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public sealed class TrustedNativeDependencyLoaderTests
{
    [Fact]
    public void FileNames_AreTheCompletePackagedNativeSet()
    {
        Assert.Equal(
            [
                "av_libglesv2.dll",
                "e_sqlite3.dll",
                "libHarfBuzzSharp.dll",
                "libSkiaSharp.dll",
            ],
            TrustedNativeDependencyLoader.FileNames);
    }

    [Fact]
    public void ExpectedDependencies_HaveDistinctUppercaseSha256Values()
    {
        Assert.Equal(4, TrustedNativeDependencyLoader.ExpectedDependencies.Count);
        Assert.Equal(
            4,
            TrustedNativeDependencyLoader.ExpectedDependencies
                .Select(static dependency => dependency.CanonicalSha256)
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.All(
            TrustedNativeDependencyLoader.ExpectedDependencies,
            static dependency => Assert.Matches("^[0-9A-F]{64}$", dependency.CanonicalSha256));
    }

    [Fact]
    public void ExpectedDependencies_MatchResolvedWinX64PackageAssets()
    {
        var nativeDirectory = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");

        Assert.All(
            TrustedNativeDependencyLoader.ExpectedDependencies,
            dependency => Assert.Equal(
                dependency.CanonicalSha256,
                AuthenticodeCanonicalHash.ComputeSha256(Path.Combine(nativeDirectory, dependency.FileName))));
    }

    [Fact]
    public void VerifyAndLoad_MissingDependency_FailsClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tipc-cig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var result = TrustedNativeDependencyLoader.VerifyAndLoad(directory);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
            Assert.Contains("av_libglesv2.dll", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void VerifyAndLoad_HashMismatch_FailsBeforeLoading()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tipc-cig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Environment.ProcessPath!, Path.Combine(directory, "av_libglesv2.dll"));

            var result = TrustedNativeDependencyLoader.VerifyAndLoad(directory);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
            Assert.Contains("baked SHA-256", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ValidateLoadedModules_AcceptsOnlyApplicationExpectedAndWindowsModules()
    {
        var applicationDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tipc-app"));
        var processPath = Path.Combine(applicationDirectory, "ThisIsMyPC.App.exe");
        var expectedPaths = TrustedNativeDependencyLoader.FileNames
            .Select(fileName => Path.Combine(applicationDirectory, fileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaded = new List<string> { processPath };
        loaded.AddRange(expectedPaths);
        loaded.Add(Path.Combine(Environment.SystemDirectory, "kernel32.dll"));
        loaded.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "WinSxS",
            "amd64_example",
            "example.dll"));

        var result = TrustedNativeDependencyLoader.ValidateLoadedModules(
            loaded,
            processPath,
            applicationDirectory,
            expectedPaths);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public void ValidateLoadedModules_RejectsUnexpectedModule()
    {
        var applicationDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tipc-app"));
        var processPath = Path.Combine(applicationDirectory, "ThisIsMyPC.App.exe");
        var expectedPaths = TrustedNativeDependencyLoader.FileNames
            .Select(fileName => Path.Combine(applicationDirectory, fileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaded = new List<string> { processPath };
        loaded.AddRange(expectedPaths);
        var unexpectedPath = Path.Combine(Path.GetTempPath(), "unexpected.dll");
        loaded.Add(unexpectedPath);

        var result = TrustedNativeDependencyLoader.ValidateLoadedModules(
            loaded,
            processPath,
            applicationDirectory,
            expectedPaths);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
        Assert.Contains(unexpectedPath, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLoadedModules_RejectsMissingExpectedModule()
    {
        var applicationDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tipc-app"));
        var processPath = Path.Combine(applicationDirectory, "ThisIsMyPC.App.exe");
        var expectedPaths = TrustedNativeDependencyLoader.FileNames
            .Select(fileName => Path.Combine(applicationDirectory, fileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loaded = new List<string> { processPath };
        loaded.AddRange(expectedPaths.Skip(1));

        var result = TrustedNativeDependencyLoader.ValidateLoadedModules(
            loaded,
            processPath,
            applicationDirectory,
            expectedPaths);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
        Assert.Contains("absent", result.ErrorMessage, StringComparison.Ordinal);
    }
}
