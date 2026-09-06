using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class MachineIdentityTests
{
    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.")]
    [InlineData("ASUS")]
    [InlineData("  asustek computer inc.  ")]
    public void AsusSpellings_MapToAsus(string manufacturer)
    {
        var identity = MachineIdentity.From(manufacturer, "ROG Strix G614JZ_G614JZ");

        Assert.Equal(MachineVendor.Asus, identity.Vendor);
        Assert.Equal(manufacturer.Trim(), identity.Manufacturer);
        Assert.Equal("ROG Strix G614JZ_G614JZ", identity.Model);
    }

    [Theory]
    [InlineData("HP")]
    [InlineData("LENOVO")]
    [InlineData("Dell Inc.")]
    [InlineData("Micro-Star International Co., Ltd.")]
    [InlineData("Framework")]
    public void NamedNonAsusVendor_IsOther(string manufacturer)
    {
        Assert.Equal(MachineVendor.Other, MachineIdentity.From(manufacturer, "Model").Vendor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    [InlineData("System manufacturer")]
    [InlineData("To Be Filled By O.E.M.")]
    [InlineData("Default string")]
    [InlineData("N/A")]
    [InlineData("OEM")]
    public void PlaceholderManufacturer_IsUnknown(string? manufacturer)
    {
        var identity = MachineIdentity.From(manufacturer, "Model");

        Assert.Equal(MachineVendor.Unknown, identity.Vendor);
        Assert.Null(identity.Manufacturer);
    }

    [Theory]
    [InlineData("System Product Name")]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("")]
    public void PlaceholderModel_IsNull(string model)
    {
        Assert.Null(MachineIdentity.From("ASUS", model).Model);
    }

    [Fact]
    public void UnknownInstance_HasNoVendor()
    {
        Assert.Equal(MachineVendor.Unknown, MachineIdentity.Unknown.Vendor);
        Assert.Null(MachineIdentity.Unknown.Manufacturer);
        Assert.Null(MachineIdentity.Unknown.Model);
    }
}
