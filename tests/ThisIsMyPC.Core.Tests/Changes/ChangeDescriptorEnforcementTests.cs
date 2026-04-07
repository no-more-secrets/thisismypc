using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;

namespace ThisIsMyPC.Core.Tests.Changes;

public sealed class ChangeDescriptorEnforcementTests
{
    private static ChangeDescriptor CreateTestChange(SettingEnforcement? enforcement = null) => new()
    {
        ModuleId = "shell",
        SettingId = "test-setting",
        DisplayName = "Test Setting",
        SystemLocation = @"HKCU\Software\Test",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "Off",
        AfterDisplay = "On",
        ValueType = ChangeValueType.Registry_DWord,
        Enforcement = enforcement,
    };

    [Fact]
    public void Enforcement_defaults_to_null()
    {
        var change = CreateTestChange();

        Assert.Null(change.Enforcement);
    }

    [Fact]
    public void Enforcement_can_be_set_to_non_null()
    {
        var enforcement = new SettingEnforcement
        {
            CompanionServices = ["DiagTrack"],
            SkuRestriction = WindowsSku.Pro,
        };

        var change = CreateTestChange(enforcement);

        Assert.NotNull(change.Enforcement);
        Assert.Equal(WindowsSku.Pro, change.Enforcement.SkuRestriction);
        Assert.Single(change.Enforcement.CompanionServices!);
    }

    [Fact]
    public void Existing_properties_unaffected_by_enforcement()
    {
        var enforcement = new SettingEnforcement { AclElevation = true };
        var change = CreateTestChange(enforcement);

        Assert.Equal("shell", change.ModuleId);
        Assert.Equal("test-setting", change.SettingId);
        Assert.Equal(ChangeValueType.Registry_DWord, change.ValueType);
        Assert.NotNull(change.Enforcement);
    }
}
