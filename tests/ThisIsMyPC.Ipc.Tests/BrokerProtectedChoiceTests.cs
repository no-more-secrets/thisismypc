using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class BrokerProtectedChoiceTests
{
    [Fact]
    public void AlternateAdministratorCannotWritePrimaryAccountProtectedChoice()
    {
        var target = RestorationCatalog.Default.Targets[0];
        var change = new ChangeDescriptor
        {
            ModuleId = target.ModuleId, SettingId = target.SettingId, DisplayName = "setting",
            SystemLocation = target.KeyPath + "\\" + target.ValueName,
            BeforeValue = "0", AfterValue = "1", BeforeDisplay = "off", AfterDisplay = "on",
            ValueType = ChangeValueType.Registry_DWord,
        };
        Assert.False(PrivilegedModuleHost.CanApplyProtectedChoice(change, "S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1002"));
        Assert.False(PrivilegedModuleHost.CanApplyProtectedChoice(change, "S-1-5-21-1-2-3-1001", null));
        Assert.True(PrivilegedModuleHost.CanApplyProtectedChoice(change, "S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1001"));
    }

    [Fact]
    public async Task SavedChoiceDoesNotRollback()
    {
        var saved = false;
        var result = await PrivilegedModuleHost.SaveProtectedChoiceAsync(() => saved = true, () => throw new InvalidOperationException("Must not disable."),
            () => throw new InvalidOperationException("Rollback must not run."));
        Assert.True(saved);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task FailedSaveRestoresAppliedValueBeforeReportingFailure()
    {
        var value = 1;
        var result = await PrivilegedModuleHost.SaveProtectedChoiceAsync(() => throw new IOException("disk"), () => { },
            () => { value = 0; return Task.FromResult(OperationResult<bool>.Success(true)); });
        Assert.Equal(0, value);
        Assert.False(result.IsSuccess);
        Assert.Contains("previous value", result.ErrorMessage);
    }

    [Fact]
    public async Task FailedRollbackReportsUncertainMutation()
    {
        var result = await PrivilegedModuleHost.SaveProtectedChoiceAsync(() => throw new IOException("disk"), () => { },
            () => Task.FromResult(OperationResult<bool>.Failure("registry refused", ErrorCategory.AccessDenied)));
        Assert.False(result.IsSuccess);
        Assert.Contains("uncertain", result.ErrorMessage);
    }
    [Fact]
    public async Task FailedDisarmDoesNotRollbackIntoAnActiveAmbiguousChoice()
    {
        var result = await PrivilegedModuleHost.SaveProtectedChoiceAsync(() => throw new IOException("disk"),
            () => throw new IOException("Cannot remove or pause"),
            () => throw new InvalidOperationException("Rollback must not run."));
        Assert.False(result.IsSuccess);
        Assert.Contains("Rollback was not attempted", result.ErrorMessage);
    }

}
