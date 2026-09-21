namespace ThisIsMyPC.Core.Hardware.Sensors;

public enum HardwareSensorComponent { Memory, Gpu, Battery }

public enum HardwareSensorUnit
{
    Celsius, Percent, Megabytes, Gigabytes, Megahertz, Watts, Rpm, Volts,
    BytesPerSecond, MilliwattHours, Milliamperes, Hours, Count
}

/// <summary>A null value means unavailable, never a measured zero.</summary>
public sealed record HardwareSensorReading(
    string Id, string DeviceId, string DeviceName, HardwareSensorComponent Component,
    string Name, HardwareSensorUnit Unit, double? Value);

/// <summary>Partial failures belong in CoverageNotes; successful devices remain readable.</summary>
public sealed record HardwareSensorSnapshot(
    DateTimeOffset CapturedAt, IReadOnlyList<HardwareSensorReading> Readings,
    IReadOnlyList<string> CoverageNotes);

/// <summary>
/// Read-only hardware access. Implementations serialize reads and release device handles on disposal.
/// Cancellation stops waiting; an in-flight native call must finish before another read starts.
/// A device failure returns partial readings and notes. Cancellation and disposal throw normally.
/// </summary>
public interface IHardwareSensorBackend : IDisposable
{
    Task<HardwareSensorSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record HardwareSensorSample(DateTimeOffset CapturedAt, double? Value);

/// <summary>Statistics cover only the retained history window. Gaps do not contribute to the average.</summary>
public sealed record HardwareSensorStatistics(
    HardwareSensorReading Reading, double? Current, double? Minimum, double? Maximum,
    double? Average, IReadOnlyList<HardwareSensorSample> History);
