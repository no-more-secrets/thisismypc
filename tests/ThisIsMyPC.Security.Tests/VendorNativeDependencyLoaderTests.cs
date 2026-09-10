using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public sealed class VendorNativeDependencyLoaderTests
{
    [Fact]
    public void Optional_IsOnlyNvApi_FromSystem32_SignedByNvidia()
    {
        var dependency = Assert.Single(VendorNativeDependencyLoader.Optional);
        Assert.Equal("nvapi64.dll", dependency.FileName);
        Assert.Equal("NVIDIA Corporation", dependency.SignerSubjectFragment);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, dependency.FileName);
    }

    [Fact]
    public void PreloadOptional_NeverThrows_AndReportsEveryLibrary()
    {
        // With or without an NVIDIA driver on the machine running this, the
        // outcome is one line per optional library and no exception.
        VendorNativeDependencyLoader.PreloadOptional();

        Assert.Contains(VendorNativeDependencyLoader.Report, line => line.StartsWith("nvapi64.dll: ", StringComparison.Ordinal));
        var line = VendorNativeDependencyLoader.Report.Last(l => l.StartsWith("nvapi64.dll: ", StringComparison.Ordinal));
        if (File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll")))
            Assert.True(line.Contains("mapped before CIG", StringComparison.Ordinal) || line.Contains("not mapped", StringComparison.Ordinal), line);
        else
            Assert.Contains("not installed", line, StringComparison.Ordinal);
    }
}
