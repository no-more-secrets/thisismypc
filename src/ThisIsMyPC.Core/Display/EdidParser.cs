using System.Text;

namespace ThisIsMyPC.Core.Display;

/// <summary>
/// Extracts the monitor's model name from its EDID block: descriptor slots at
/// offsets 54/72/90/108, the one tagged 0xFC carries up to 13 ASCII chars
/// terminated by 0x0A. This is the name the OSD shows, not the driver's
/// "Generic PnP Monitor".
/// </summary>
public static class EdidParser
{
    public static string? ParseMonitorName(byte[]? edid)
    {
        if (edid is null || edid.Length < 128)
            return null;

        foreach (var offset in (int[])[54, 72, 90, 108])
        {
            // Display descriptor: first two bytes zero, byte 3 is the tag.
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 3] != 0xFC)
                continue;

            var raw = Encoding.ASCII.GetString(edid, offset + 5, 13);
            var newline = raw.IndexOf('\n');
            var name = (newline >= 0 ? raw[..newline] : raw).Trim();
            return name.Length > 0 ? name : null;
        }

        return null;
    }
}
