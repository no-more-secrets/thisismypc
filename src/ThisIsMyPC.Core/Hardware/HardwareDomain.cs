namespace ThisIsMyPC.Core.Hardware;

/// <summary>
/// One Hardware tab whose availability the compatibility policy decides.
/// Display is not listed: it ships already and gates itself per monitor.
/// </summary>
public enum HardwareDomain
{
    /// <summary>Laptop performance modes, battery limits, keyboard and platform controls (G-Helper territory).</summary>
    SystemControl,

    /// <summary>RGB lighting through OpenRGB.</summary>
    Lighting,

    /// <summary>Fan control through FanControl.</summary>
    Cooling,

    /// <summary>Curated sensor readout through LibreHardwareMonitor.</summary>
    Monitoring,
}
