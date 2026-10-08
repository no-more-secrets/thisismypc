using ThisIsMyPC.Modules.Software.Actions;
using ThisIsMyPC.Modules.Software.Services;
using ThisIsMyPC.Modules.Software.Tests.Fakes;
namespace ThisIsMyPC.Modules.Software.Tests;
public sealed class CatalogDownloadTests
{
    [Fact]
    public async Task WebsiteOnlyInstallCannotReachWinget()
    {
        var winget = new FakeWingetService();
        var module = new SoftwareModule(winget, new FakeAppxPackageService());
        var result = await module.ExecuteActionAsync(SoftwareActionFactory.CreateInstall(SoftwareCatalog.Entries.Single(e => e.Id == "spotifast")));
        Assert.False(result.IsSuccess);
        Assert.Empty(winget.Installs);
    }
}
