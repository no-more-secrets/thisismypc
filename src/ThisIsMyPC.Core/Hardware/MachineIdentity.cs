namespace ThisIsMyPC.Core.Hardware;

/// <summary>Vendor families the policy tells apart. Other means "named, but no vendor-specific policy".</summary>
public enum MachineVendor
{
    Unknown,
    Asus,
    Other,
}

/// <summary>
/// Normalized manufacturer and model. Built from the same registry strings the
/// Home tab shows (SystemIdentity.Manufacturer and Model); firmware placeholders
/// such as "To Be Filled By O.E.M." collapse to null so nothing downstream
/// mistakes them for a real vendor.
/// </summary>
public sealed record MachineIdentity
{
    private static readonly string[] Placeholders =
    [
        "unknown",
        "system manufacturer",
        "system product name",
        "to be filled by o.e.m.",
        "default string",
        "not applicable",
        "n/a",
        "none",
        "oem",
    ];

    public static MachineIdentity Unknown { get; } = new(null, null);

    private MachineIdentity(string? manufacturer, string? model)
    {
        Manufacturer = manufacturer;
        Model = model;
        Vendor = manufacturer is null ? MachineVendor.Unknown
            : IsAsus(manufacturer) ? MachineVendor.Asus
            : MachineVendor.Other;
    }

    /// <summary>Manufacturer as reported, trimmed; null when absent or a placeholder.</summary>
    public string? Manufacturer { get; }

    /// <summary>Model (SystemProductName) as reported, trimmed; null when absent or a placeholder.</summary>
    public string? Model { get; }

    public MachineVendor Vendor { get; }

    public static MachineIdentity From(string? manufacturer, string? model)
        => new(Normalize(manufacturer), Normalize(model));

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return Placeholders.Contains(trimmed, StringComparer.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static bool IsAsus(string manufacturer)
        => manufacturer.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase);
}
