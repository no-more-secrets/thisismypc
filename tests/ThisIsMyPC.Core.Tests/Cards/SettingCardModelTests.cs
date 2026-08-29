using ThisIsMyPC.Core.Cards;

namespace ThisIsMyPC.Core.Tests.Cards;

public sealed class SettingCardModelTests
{
    private static SettingCardModel Minimal() => new()
    {
        SettingId = "s",
        ModuleId = "m",
        DisplayName = "Setting",
        Description = "Does a thing.",
        ControlType = SettingControlType.Toggle,
        CurrentValue = "1",
    };

    [Fact]
    public void OptionalFields_DefaultToAbsent()
    {
        var model = Minimal();

        Assert.Null(model.CurrentDisplayValue);
        Assert.Null(model.AvailableOptions);
        Assert.Null(model.RegistryPath);
        Assert.Null(model.ValueName);
        Assert.Null(model.RegistryValueType);
        Assert.Null(model.GroupId);
        Assert.Null(model.Enforcement);
        Assert.False(model.OwnerModeRequired);
        Assert.Null(model.SkuRestriction);
    }

    [Fact]
    public void ControlTypeEnum_CoversArchitectureSet()
    {
        Assert.Equal(
            ["Toggle", "Dropdown", "Slider", "Action"],
            Enum.GetNames<SettingControlType>());
    }

    [Fact]
    public void EnforcementLevelEnum_CoversArchitectureSet()
    {
        Assert.Equal(
            ["None", "Simple", "Enforced", "OwnerRequired"],
            Enum.GetNames<EnforcementLevel>());
    }

    [Fact]
    public void SettingOption_IsValueDisplayPair()
    {
        var option = new SettingOption("2", "Notify only");

        Assert.Equal("2", option.Value);
        Assert.Equal("Notify only", option.DisplayName);
    }
}
