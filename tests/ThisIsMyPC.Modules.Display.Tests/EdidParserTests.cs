using System.Text;
using ThisIsMyPC.Core.Display;

namespace ThisIsMyPC.Modules.Display.Tests;

public sealed class EdidParserTests
{
    /// <summary>A 128-byte EDID with one 0xFC descriptor carrying the given name.</summary>
    private static byte[] EdidWithName(string name, int slot = 54)
    {
        var edid = new byte[128];
        edid[slot] = 0;
        edid[slot + 1] = 0;
        edid[slot + 3] = 0xFC;
        var text = name.Length < 13 ? name + "\n" : name[..13];
        Encoding.ASCII.GetBytes(text).CopyTo(edid, slot + 5);
        return edid;
    }

    [Fact]
    public void Parses_the_name_from_the_first_descriptor()
    {
        Assert.Equal("VG27AQ", EdidParser.ParseMonitorName(EdidWithName("VG27AQ")));
    }

    [Fact]
    public void Finds_the_name_descriptor_in_a_later_slot()
    {
        Assert.Equal("PA278QV", EdidParser.ParseMonitorName(EdidWithName("PA278QV", slot: 90)));
    }

    [Fact]
    public void Thirteen_char_names_have_no_terminator()
    {
        Assert.Equal("ABCDEFGHIJKLM", EdidParser.ParseMonitorName(EdidWithName("ABCDEFGHIJKLM")));
    }

    [Fact]
    public void Null_short_or_nameless_blocks_yield_null()
    {
        Assert.Null(EdidParser.ParseMonitorName(null));
        Assert.Null(EdidParser.ParseMonitorName(new byte[64]));
        Assert.Null(EdidParser.ParseMonitorName(new byte[128]));
    }
}
