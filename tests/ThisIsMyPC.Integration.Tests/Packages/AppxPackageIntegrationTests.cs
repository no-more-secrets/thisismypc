using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Packages;

namespace ThisIsMyPC.Integration.Tests.Packages;

/// <summary>
/// Read-only deployment-stack queries. No mutations — these tests must never remove or
/// deprovision a package on the machine running them.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AppxPackageIntegrationTests
{
    private readonly AppxPackageService _sut = new();

    [Fact]
    public async Task EnumeratePackages_ReturnsCoherentNonEmptyList()
    {
        var result = await _sut.EnumeratePackagesAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotEmpty(result.Value!);
        Assert.All(result.Value!, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.PackageFullName));
            Assert.False(string.IsNullOrWhiteSpace(p.PackageFamilyName));
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(p.Version));
        });
        // Every Windows install carries Microsoft-published packages.
        Assert.Contains(result.Value!, p =>
            p.PackageFamilyName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task QueryPackage_FirstEnumeratedPackage_RoundTrips()
    {
        var all = await _sut.EnumeratePackagesAsync();
        Assert.True(all.IsSuccess, all.ErrorMessage);
        var expected = all.Value![0];

        var result = await _sut.QueryPackageAsync(expected.PackageFullName);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(expected.PackageFullName, result.Value!.PackageFullName);
        Assert.Equal(expected.PackageFamilyName, result.Value.PackageFamilyName);
    }

    [Fact]
    public async Task QueryPackage_NonexistentFullName_ReturnsNotFound()
    {
        var result = await _sut.QueryPackageAsync(
            "ThisIsMyPC.NoSuchPackage_1.0.0.0_x64__0000000000000");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
    }
}
