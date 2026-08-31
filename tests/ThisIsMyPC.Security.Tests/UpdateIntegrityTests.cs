using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public class UpdateIntegrityTests
{
    [Fact]
    public void IUpdateVerifier_IsAsync_TakesVersionAndPackagePath()
    {
        var method = typeof(IUpdateVerifier).GetMethod(nameof(IUpdateVerifier.VerifyPackageAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<OperationResult<bool>>), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);            // updateVersion
        Assert.Equal(typeof(string), parameters[1].ParameterType);            // packageFilePath (nullable)
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
    }

    [Fact]
    public void IUpdateService_HasCheckDownloadApplyMethods()
    {
        var checkMethod = typeof(IUpdateService).GetMethod(nameof(IUpdateService.CheckForUpdateAsync));
        Assert.NotNull(checkMethod);

        var downloadMethod = typeof(IUpdateService).GetMethod(nameof(IUpdateService.DownloadUpdateAsync));
        Assert.NotNull(downloadMethod);

        var applyMethod = typeof(IUpdateService).GetMethod(nameof(IUpdateService.ApplyUpdateAndRestart));
        Assert.NotNull(applyMethod);
    }

    [Fact]
    public void UpdateCheckResult_NoUpdate_HasIsAvailableFalse()
    {
        var result = new UpdateCheckResult(false, null, null);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Version);
    }

    [Fact]
    public void UpdateCheckResult_UpdateAvailable_HasVersionAndIsAvailableTrue()
    {
        var result = new UpdateCheckResult(true, "2.0.0", "Bug fixes");
        Assert.True(result.IsAvailable);
        Assert.Equal("2.0.0", result.Version);
        Assert.Equal("Bug fixes", result.ReleaseNotes);
    }
}
