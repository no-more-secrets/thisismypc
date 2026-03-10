using ThisIsMyPC.Interop.Com.Shell;

namespace ThisIsMyPC.Integration.Tests.Shell;

public class ModernPackagedIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void EnumerateModernHandlers_returns_success()
    {
        var service = new ModernPackagedHandlerService();
        var result = service.EnumerateModernHandlers();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void EnumerateModernHandlers_entries_have_valid_data()
    {
        var service = new ModernPackagedHandlerService();
        var result = service.EnumerateModernHandlers();

        Assert.True(result.IsSuccess);

        foreach (var entry in result.Value!)
        {
            Assert.False(string.IsNullOrEmpty(entry.Clsid));
            Assert.False(string.IsNullOrEmpty(entry.PackageFamilyName));
            Assert.False(string.IsNullOrEmpty(entry.PackageDisplayName));
            Assert.False(string.IsNullOrEmpty(entry.PublisherDisplayName));
        }
    }
}
