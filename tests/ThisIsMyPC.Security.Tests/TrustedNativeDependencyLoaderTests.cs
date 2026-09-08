using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Results;

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
}
