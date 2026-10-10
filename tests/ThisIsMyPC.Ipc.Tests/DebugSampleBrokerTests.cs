using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class DebugSampleBrokerTests
{
    [Fact]
    public async Task SampleChangeCanBeAppliedAndUndoneAcrossBuilds()
    {
        var change = Sample();
        var policy = BrokerRequestPolicy.Create(new() { Changes = [change] });
        Assert.True(policy.IsSuccess);
        Assert.True(policy.Value!.Authorizes(new()
        {
            Kind = BrokerCommandKind.ApplyChange,
            Change = change,
        }));
        Assert.True((await DebugSampleChange.Apply(change)).IsSuccess);
        var inverse = change with
        {
            BeforeValue = change.AfterValue!,
            AfterValue = change.BeforeValue,
            BeforeDisplay = change.AfterDisplay!,
            AfterDisplay = change.BeforeDisplay,
        };
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [inverse] }).IsSuccess);
        Assert.True((await DebugSampleChange.Apply(inverse)).IsSuccess);
    }

    [Fact]
    public void DebugSampleCannotNameARealSystemTarget()
    {
        var change = Sample();
        Assert.False(BrokerRequestPolicy.Create(new()
        {
            Changes = [change with { SystemLocation = @"HKLM\SOFTWARE\Debug\Setting1" }],
        }).IsSuccess);
    }

    [Theory]
    [InlineData(ChangeCategory.Disable, "1", "0", "Enabled", "Disabled")]
    [InlineData(ChangeCategory.Modify, "0", "1", "Value A", "Value B")]
    public async Task OtherSampleCategoriesUseTheSameNoWritePath(ChangeCategory category,
        string before, string after, string beforeDisplay, string afterDisplay)
    {
        var change = Sample() with
        {
            Category = category,
            BeforeValue = before,
            AfterValue = after,
            BeforeDisplay = beforeDisplay,
            AfterDisplay = afterDisplay,
        };

        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        Assert.True((await DebugSampleChange.Apply(change)).IsSuccess);
        Assert.True((await DebugSampleChange.Apply(change with
        {
            BeforeValue = after,
            AfterValue = before,
            BeforeDisplay = afterDisplay,
            AfterDisplay = beforeDisplay,
        })).IsSuccess);
    }

    private static ChangeDescriptor Sample() => new()
    {
        ModuleId = "DebugModule",
        SettingId = "debug-setting-1",
        DisplayName = "Test Setting 1",
        SystemLocation = "No system target (Debug sample)",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Disabled",
        AfterDisplay = "Enabled",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Enable,
    };
}
