using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Tests.Enforcement;

public sealed class EnforcementStepTypeTests
{
    [Fact]
    public void HasAllTenValues()
    {
        var values = Enum.GetValues<EnforcementStepType>();

        Assert.Equal(10, values.Length);
        Assert.Contains(EnforcementStepType.DisableService, values);
        Assert.Contains(EnforcementStepType.EnableService, values);
        Assert.Contains(EnforcementStepType.DisableScheduledTask, values);
        Assert.Contains(EnforcementStepType.EnableScheduledTask, values);
        Assert.Contains(EnforcementStepType.ClearGPCache, values);
        Assert.Contains(EnforcementStepType.RestoreGPCache, values);
        Assert.Contains(EnforcementStepType.PrimaryMutation, values);
        Assert.Contains(EnforcementStepType.VerifyPostMutation, values);
        Assert.Contains(EnforcementStepType.TransferOwnership, values);
        Assert.Contains(EnforcementStepType.RestoreOwnership, values);
    }
}

public sealed class EnforcementStepResultTests
{
    [Fact]
    public void Construction_RequiredFieldsSet()
    {
        var step = new EnforcementStepResult
        {
            StepType = EnforcementStepType.DisableService,
            Target = "WbioSrvc",
            IsSuccess = true
        };

        Assert.Equal(EnforcementStepType.DisableService, step.StepType);
        Assert.Equal("WbioSrvc", step.Target);
        Assert.True(step.IsSuccess);
        Assert.Null(step.ErrorMessage);
        Assert.False(step.WasRolledBack);
    }

    [Fact]
    public void Construction_FailureWithRollback()
    {
        var step = new EnforcementStepResult
        {
            StepType = EnforcementStepType.ClearGPCache,
            Target = @"SOFTWARE\Policies\Test",
            IsSuccess = false,
            ErrorMessage = "access denied",
            WasRolledBack = true
        };

        Assert.False(step.IsSuccess);
        Assert.Equal("access denied", step.ErrorMessage);
        Assert.True(step.WasRolledBack);
    }
}

public sealed class EnforcementResultTests
{
    [Fact]
    public void Construction_StepsDefaultsToEmpty()
    {
        var result = new EnforcementResult { IsSuccess = true };

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Steps);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorCategory);
    }

    [Fact]
    public void Construction_FailureCarriesStepsAndError()
    {
        var result = new EnforcementResult
        {
            IsSuccess = false,
            ErrorMessage = "step 2 failed",
            ErrorCategory = ErrorCategory.ServiceUnavailable,
            Steps =
            [
                new EnforcementStepResult
                {
                    StepType = EnforcementStepType.DisableService,
                    Target = "WSearch",
                    IsSuccess = true,
                    WasRolledBack = true
                },
                new EnforcementStepResult
                {
                    StepType = EnforcementStepType.DisableScheduledTask,
                    Target = @"\Microsoft\Windows\Maps\MapsUpdateTask",
                    IsSuccess = false,
                    ErrorMessage = "step 2 failed"
                }
            ]
        };

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
    }
}
