using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.App.UiTests;

public class VelopackUninstalledTests
{
    [Fact]
    public async Task UnpackagedRunSkipsTheUpdateFeed()
    {
        using var service = new VelopackUpdateService(new Velopack.UpdateManager("http://127.0.0.1:1",
            locator: Velopack.Locators.VelopackLocator.CreateDefaultForPlatform()));
        var result = await service.CheckForUpdateAsync();
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.Value!.IsAvailable);
    }
}
