using System.Globalization;

namespace ThisIsMyPC.Core.Display;

/// <summary>
/// Minimal parser for the MCCS capabilities string a monitor returns over
/// DDC/CI, e.g. "(prot(monitor)type(LCD)vcp(02 10 12 60(0F 11 12) 62)...)".
/// Only the piece the Display module needs: the allowed values of VCP 0x60
/// (input source).
/// </summary>
public static class VcpCapabilities
{
    /// <summary>
    /// The values listed for VCP code 60 inside the vcp(...) section, low byte
    /// only. Empty when the string is malformed, has no vcp section, or lists
    /// 60 without a value group.
    /// </summary>
    public static IReadOnlyList<int> ParseInputSourceValues(string capabilities)
    {
        var vcpBody = ExtractSection(capabilities, "vcp");
        if (vcpBody is null)
            return [];

        var i = 0;
        while (i < vcpBody.Length)
        {
            if (char.IsWhiteSpace(vcpBody[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < vcpBody.Length && IsHexDigit(vcpBody[i]))
                i++;

            var token = vcpBody[start..i];
            var hasGroup = i < vcpBody.Length && vcpBody[i] == '(';
            var groupBody = hasGroup ? ExtractParenGroup(vcpBody, ref i) : null;

            if (token.Length == 0)
            {
                // Not a hex token and not a group opener: malformed tail; stop.
                if (!hasGroup)
                    break;
                continue;
            }

            if (token.Equals("60", StringComparison.OrdinalIgnoreCase) && groupBody is not null)
                return ParseHexList(groupBody);
        }

        return [];
    }

    private static string? ExtractSection(string text, string name)
    {
        var marker = name + "(";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        var i = index + marker.Length - 1;
        return ExtractParenGroup(text, ref i);
    }

    /// <summary>Reads a balanced (...) group starting at text[i] == '('; advances i past it.</summary>
    private static string? ExtractParenGroup(string text, ref int i)
    {
        if (i >= text.Length || text[i] != '(')
            return null;

        var depth = 0;
        var start = i + 1;
        for (; i < text.Length; i++)
        {
            if (text[i] == '(')
                depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    var body = text[start..i];
                    i++;
                    return body;
                }
            }
        }

        return null; // unbalanced
    }

    private static List<int> ParseHexList(string body)
    {
        var values = new List<int>();
        foreach (var token in body.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                values.Add(value & 0xFF);
        }

        return values;
    }

    private static bool IsHexDigit(char c) =>
        c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
}
