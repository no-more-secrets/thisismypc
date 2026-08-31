namespace ThisIsMyPC.Core.Display;

/// <summary>One selectable input source of a DDC monitor (VCP 0x60 value).</summary>
public sealed record MonitorInputSource(int Value, string Name);

/// <summary>
/// One physical display as the Display module presents it. External monitors
/// come from DDC/CI enumeration; the built-in panel of a laptop is a synthetic
/// entry controlled through the active power plan's brightness setting.
/// </summary>
public sealed record MonitorDevice
{
    /// <summary>Stable within one enumeration: adapter device name + physical index.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool IsInternalPanel { get; init; }

    /// <summary>False when the monitor did not answer DDC/CI; controls render disabled.</summary>
    public bool SupportsDdc { get; init; }

    public int Brightness { get; init; }
    public int BrightnessMax { get; init; } = 100;

    /// <summary>Null when the monitor does not expose contrast (internal panels never do).</summary>
    public int? Contrast { get; init; }
    public int ContrastMax { get; init; } = 100;

    /// <summary>Current VCP 0x60 value; null when unknown or not applicable.</summary>
    public int? CurrentInput { get; init; }

    public IReadOnlyList<MonitorInputSource> InputSources { get; init; } = [];

    /// <summary>Why DDC is unavailable, for the card's degraded note.</summary>
    public string? DdcError { get; init; }
}
