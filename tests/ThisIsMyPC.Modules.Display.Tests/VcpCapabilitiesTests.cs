using ThisIsMyPC.Core.Display;

namespace ThisIsMyPC.Modules.Display.Tests;

public sealed class VcpCapabilitiesTests
{
    private const string RealWorldSample =
        "(prot(monitor)type(LCD)model(VG27AQ)cmds(01 02 03 07 0C E3 F3)" +
        "vcp(02 04 05 08 0B 0C 10 12 14(05 08 0B) 16 18 1A 60(0F 11 12) 62 6C 6E 70 " +
        "86(02 0B) 8D(01 02) AC AE B2 B6 C6 C8 C9 CA(01 02) CC(01 02 03 04 05 06 07 08 09 0A 0D 12 14 16 17 1A 1E) " +
        "D6(01 04 05) DF E0(00 01 02 03 04) E1(00 01))mswhql(1)asset_eep(40)mccs_ver(2.2))";

    [Fact]
    public void Parses_input_values_from_a_real_capabilities_string()
    {
        var values = VcpCapabilities.ParseInputSourceValues(RealWorldSample);
        Assert.Equal([0x0F, 0x11, 0x12], values);
    }

    [Fact]
    public void Sixty_without_a_value_group_yields_nothing()
    {
        var values = VcpCapabilities.ParseInputSourceValues("(vcp(10 12 60 62))");
        Assert.Empty(values);
    }

    [Fact]
    public void Missing_vcp_section_yields_nothing()
    {
        Assert.Empty(VcpCapabilities.ParseInputSourceValues("(prot(monitor)type(LCD))"));
        Assert.Empty(VcpCapabilities.ParseInputSourceValues(""));
    }

    [Fact]
    public void Unbalanced_string_yields_nothing_instead_of_throwing()
    {
        Assert.Empty(VcpCapabilities.ParseInputSourceValues("(vcp(10 60(0F 11"));
    }

    [Fact]
    public void Lowercase_hex_and_high_bytes_normalize()
    {
        var values = VcpCapabilities.ParseInputSourceValues("(vcp(60(0f 1b)))");
        Assert.Equal([0x0F, 0x1B], values);
    }

    [Fact]
    public void Other_codes_value_groups_are_not_mistaken_for_inputs()
    {
        // 160 is a different token even though it ends in "60".
        var values = VcpCapabilities.ParseInputSourceValues("(vcp(14(05 08) 160(01) 60(11)))");
        Assert.Equal([0x11], values);
    }
}
