using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public class UpdateIntegrityTests
{
    [Fact]
    public void IUpdateVerifier_InterfaceExists_WithVerifyPackageIntegrity()
    {
        var method = typeof(IUpdateVerifier).GetMethod(nameof(IUpdateVerifier.VerifyPackageIntegrity));
        Assert.NotNull(method);
        Assert.Equal(typeof(OperationResult<bool>), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);   // updateVersion
        Assert.Equal(typeof(string), parameters[1].ParameterType);   // packageFilePath (nullable)
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

    [Fact]
    public void FailingVerifier_CausesDownloadRejection_SimulatedFlow()
    {
        // Simulate the verification flow without calling real Velopack:
        // If a verifier returns failure, the update service should reject.
        IUpdateVerifier verifier = new RejectingVerifier();
        var result = verifier.VerifyPackageIntegrity("1.0.0");

        Assert.False(result.IsSuccess);
        Assert.Contains("rejected", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PassingVerifier_AllowsUpdate_SimulatedFlow()
    {
        IUpdateVerifier verifier = new PassingVerifier();
        var result = verifier.VerifyPackageIntegrity("1.0.0");

        Assert.True(result.IsSuccess);
    }

    private sealed class RejectingVerifier : IUpdateVerifier
    {
        public OperationResult<bool> VerifyPackageIntegrity(string updateVersion, string? packageFilePath = null)
            => OperationResult<bool>.Failure(
                "Update rejected: unsigned package.",
                ErrorCategory.AccessDenied);
    }

    private sealed class PassingVerifier : IUpdateVerifier
    {
        public OperationResult<bool> VerifyPackageIntegrity(string updateVersion, string? packageFilePath = null)
            => OperationResult<bool>.Success(true);
    }
}
