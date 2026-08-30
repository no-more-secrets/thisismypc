using ThisIsMyPC.Interop.Win32.Packages;

namespace ThisIsMyPC.Modules.Software.Tests;

/// <summary>
/// Runs the real winget against the live machine to prove the upgrade table
/// parser holds up outside hand-written fixtures. Read-only; excluded from CI.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class WingetUpgradeLiveDiagnosticTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public WingetUpgradeLiveDiagnosticTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ListUpgradable_ParsesTheLiveTable()
    {
        var service = new WingetService();

        var result = await service.ListUpgradableAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        _output.WriteLine($"{result.Value!.Count} upgradable packages parsed");
        foreach (var package in result.Value!)
        {
            Assert.False(string.IsNullOrWhiteSpace(package.PackageId));
            Assert.DoesNotContain(' ', package.PackageId);
            Assert.False(string.IsNullOrWhiteSpace(package.InstalledVersion));
            Assert.False(string.IsNullOrWhiteSpace(package.AvailableVersion));
        }
    }
}
