using System.Text.RegularExpressions;

namespace ThisIsMyPC.Core.Hardware;

/// <summary>Recognizes explicit chipset names. Never converts a generic PCI bridge or CPU family into a chipset.</summary>
public static partial class ChipsetIdentityResolver
{
    public static ChipsetIdentity Resolve(string? boardProduct, IReadOnlyList<HardwareDevice> devices)
    {
        var names = devices.Where(d => d.ClassName.Equals("System", StringComparison.OrdinalIgnoreCase)
                && d.HardwareIds.Any(id => id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)))
            .Where(d => d.Name.Contains("chipset", StringComparison.OrdinalIgnoreCase)
                || d.Name.Contains("LPC", StringComparison.OrdinalIgnoreCase))
            .Select(d => Find(d.Name)).Where(n => n is not null).Distinct().ToArray();
        if (names.Length == 1) return new(names[0], "Windows chipset device description");
        if (names.Length > 1) return new(null, "Conflicting chipset device descriptions");
        var inferred = Find(boardProduct);
        return inferred is null ? ChipsetIdentity.Unknown : new(inferred, "Inferred from motherboard model");
    }

    private static string? Find(string? value)
    {
        if (value is null) return null;
        var matches = ChipsetName().Matches(value).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    // Explicit product tokens only. The optional M/I suffix is a board form-factor suffix.
    [GeneratedRegex(@"(?<![A-Z0-9])(X870E|X870|X670E|X670|X570|X470|X370|B850|B650E|B650|B550|B450|B350|A620|A520|A320|TRX50|TRX40|WRX90|WRX80|Z890|Z790|Z690|Z590|Z490|Z390|Z370|Z270|Z170|B860|B760|B660|B560|B460|B365|B360|B250|B150|H810|H770|H670|H610|H570|H510|H470|H410|H370|H310|H270|H170|H110|X299|X99)(?:[MI])?(?![A-Z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChipsetName();
}
