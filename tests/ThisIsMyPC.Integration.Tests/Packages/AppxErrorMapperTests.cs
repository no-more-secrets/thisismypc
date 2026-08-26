using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Packages;

namespace ThisIsMyPC.Integration.Tests.Packages;

/// <summary>Pure mapping tests — no trait, so they run in the CI filter.</summary>
public sealed class AppxErrorMapperTests
{
    [Theory]
    [InlineData(AppxErrorMapper.E_ACCESSDENIED, ErrorCategory.AccessDenied)]
    [InlineData(AppxErrorMapper.ERROR_INSTALL_PACKAGE_NOT_FOUND, ErrorCategory.NotFound)]
    [InlineData(AppxErrorMapper.ERROR_NOT_SUPPORTED, ErrorCategory.ProtectedByPolicy)]
    [InlineData(unchecked((int)0x80073CFA), ErrorCategory.ServiceUnavailable)] // ERROR_REMOVE_FAILED
    [InlineData(unchecked((int)0x80004005), ErrorCategory.ServiceUnavailable)] // E_FAIL
    public void Map_KnownHResults_MapToExpectedCategory(int hresult, ErrorCategory expected)
    {
        var (category, message) = AppxErrorMapper.Map(hresult, "Some.Package_abc", "remove");

        Assert.Equal(expected, category);
        Assert.Contains("Some.Package_abc", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_UnknownHResult_IncludesHexCode()
    {
        var (_, message) = AppxErrorMapper.Map(unchecked((int)0x80073CFA), "pkg", "remove");

        Assert.Contains("0x80073CFA", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_NonRemovablePackage_MessageExplainsProtection()
    {
        var (_, message) = AppxErrorMapper.Map(AppxErrorMapper.ERROR_NOT_SUPPORTED, "pkg", "remove");

        Assert.Contains("non-removable", message, StringComparison.Ordinal);
    }
}
