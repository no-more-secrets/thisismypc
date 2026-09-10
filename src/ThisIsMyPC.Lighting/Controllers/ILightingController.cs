using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Lighting.Controllers;

/// <summary>
/// One device the built-in controllers drive. The port of one OpenRGB
/// RGBController: it owns the mode list with the current settings and the
/// LED colors, and it turns the session's calls into transport traffic.
/// Calls are synchronous and serialized by the backend; nothing here is
/// thread-safe on its own.
/// </summary>
public interface ILightingController : IDisposable
{
    /// <summary>The controller family, for the evidence list ("ENE SMBus", "Sinowealth").</summary>
    string Family { get; }

    /// <summary>The device as the page sees it now. <paramref name="index"/> is its position in the session's list.</summary>
    LightingDevice Describe(int index);

    /// <summary>Switches modes and applies the settings the mode carries (speed, brightness, direction, mode colors).</summary>
    OperationResult<bool> SetMode(LightingMode mode);

    /// <summary>Writes every LED, in the active mode.</summary>
    OperationResult<bool> SetLeds(IReadOnlyList<RgbColor> colors);

    OperationResult<bool> SetZoneLeds(int zoneIndex, IReadOnlyList<RgbColor> colors);

    /// <summary>Stores the current mode in the device so it survives a power cycle.</summary>
    OperationResult<bool> SaveMode(LightingMode mode);
}
