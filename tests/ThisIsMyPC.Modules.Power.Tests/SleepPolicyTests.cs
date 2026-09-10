using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Services;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests;

/// <summary>The system-wide "Allow sleep" switch over the ALLOWSTANDBY policy values.</summary>
public sealed class SleepPolicyTests
{
    private const string Key = PowerPlanChangeFactory.AllowStandbyPolicyKeyPath;
    private const string Ac = PowerPlanChangeFactory.PluggedInIndexValueName;
    private const string Dc = PowerPlanChangeFactory.OnBatteryIndexValueName;

    private readonly FakePowerService _power = new();
    private readonly FakeRegistryService _registry = new();

    private PowerModule Module => new(_power, _registry);

    [Fact]
    public void KeyPath_IsTheAllowStandbyOverrideUnderThePowerPolicyKey()
    {
        Assert.Equal(
            @"HKLM\SOFTWARE\Policies\Microsoft\Power\PowerSettings\abfc2519-3608-4c2a-94ea-171b0ed546ab",
            Key);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, null, false)]
    [InlineData(null, 0, false)]
    [InlineData(0, 0, false)]
    public void AllowsSleep_UnlessAnyPolicyValueIsZero(int? ac, int? dc, bool expected)
    {
        Assert.Equal(expected, new SleepPolicy(ac, dc).AllowsSleep);
    }

    [Fact]
    public void Blocking_StagesOneDWordZeroPerPowerSource()
    {
        var group = PowerPlanChangeFactory.CreateAllowSleepToggle(SleepPolicy.Unset, allow: false);

        Assert.Equal("Block sleep", group.DisplayName);
        Assert.Collection(group.Changes,
            ac => AssertSleepChange(ac, Ac, before: "", after: "0"),
            dc => AssertSleepChange(dc, Dc, before: "", after: "0"));
        Assert.All(group.Changes, c =>
        {
            Assert.Equal(ChangeCategory.Disable, c.Category);
            Assert.Equal("Allowed", c.BeforeDisplay);
            Assert.Equal("Blocked by policy", c.AfterDisplay);
        });
    }

    [Fact]
    public void Allowing_DeletesBothValuesAndRecordsEachBeforeState()
    {
        var group = PowerPlanChangeFactory.CreateAllowSleepToggle(new SleepPolicy(1, 0), allow: true);

        Assert.Equal("Allow sleep", group.DisplayName);
        Assert.Collection(group.Changes,
            ac => AssertSleepChange(ac, Ac, before: "1", after: ""),
            dc => AssertSleepChange(dc, Dc, before: "0", after: ""));
        Assert.Equal("Allowed", group.Changes[0].BeforeDisplay);
        Assert.Equal("Blocked by policy", group.Changes[1].BeforeDisplay);
        Assert.All(group.Changes, c => Assert.Equal(ChangeCategory.Enable, c.Category));
    }

    [Fact]
    public void Blocking_LeavesOutAValueAlreadyAtZero()
    {
        var group = PowerPlanChangeFactory.CreateAllowSleepToggle(new SleepPolicy(0, null), allow: false);

        var only = Assert.Single(group.Changes);
        AssertSleepChange(only, Dc, before: "", after: "0");
    }

    [Fact]
    public void Toggle_WithNothingToChangeStillCarriesBothValues()
    {
        var group = PowerPlanChangeFactory.CreateAllowSleepToggle(SleepPolicy.Unset, allow: true);

        Assert.Equal(2, group.Changes.Count);
    }

    private static void AssertSleepChange(ChangeDescriptor change, string valueName, string before, string after)
    {
        Assert.Equal(PowerPlanChangeFactory.AllowSleepSettingId, change.SettingId);
        Assert.Equal(PowerPlanChangeFactory.ModuleId, change.ModuleId);
        Assert.Equal($@"{Key}\{valueName}", change.SystemLocation);
        Assert.Equal(before, change.BeforeValue);
        Assert.Equal(after, change.AfterValue);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.Equal(RestartRequirement.Reboot, change.RestartRequirement);
        Assert.Null(change.Enforcement);
    }

    [Fact]
    public async Task Scan_ReadsUnsetPolicyAsAllowed()
    {
        var result = await Module.ScanSystemStateAsync();

        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.Equal(SleepPolicy.Unset, data.SleepPolicy);
        Assert.True(data.SleepPolicy!.AllowsSleep);
    }

    [Fact]
    public async Task Scan_ReadsBothPolicyValues()
    {
        _registry.SetDWord(Key, Ac, 0);
        _registry.SetDWord(Key, Dc, 1);

        var result = await Module.ScanSystemStateAsync();

        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.Equal(new SleepPolicy(0, 1), data.SleepPolicy);
        Assert.False(data.SleepPolicy!.AllowsSleep);
    }

    [Fact]
    public void ReadSleepPolicy_IsNullWhenTheReadFailsForAnotherReason()
    {
        var registry = new FakeRegistryService { ReadDWordFailure = ErrorCategory.AccessDenied };

        Assert.Null(PowerPlanScanner.ReadSleepPolicy(registry));
    }

    [Fact]
    public async Task Apply_BlockWritesZeroToBothValues()
    {
        var group = PowerPlanChangeFactory.CreateAllowSleepToggle(SleepPolicy.Unset, allow: false);

        foreach (var change in group.Changes)
            Assert.True((await Module.ApplyChangeAsync(change)).IsSuccess);

        Assert.Equal(0, _registry.GetDWord(Key, Ac));
        Assert.Equal(0, _registry.GetDWord(Key, Dc));
    }

    [Fact]
    public async Task Revert_SwappedDescriptorsDeleteTheValuesAgain()
    {
        _registry.SetDWord(Key, Ac, 0);
        _registry.SetDWord(Key, Dc, 0);
        var group = PowerPlanChangeFactory.CreateAllowSleepToggle(SleepPolicy.Unset, allow: false);

        foreach (var change in group.Changes)
        {
            var swapped = change with { BeforeValue = change.AfterValue, AfterValue = change.BeforeValue };
            Assert.True((await Module.RevertChangeAsync(swapped)).IsSuccess);
        }

        Assert.Null(_registry.GetDWord(Key, Ac));
        Assert.Null(_registry.GetDWord(Key, Dc));
    }
}
