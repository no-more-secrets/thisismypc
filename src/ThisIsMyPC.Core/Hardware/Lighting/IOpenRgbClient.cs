using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>
/// Opens sessions against an OpenRGB SDK server (a copy the person runs
/// themselves). The Win32 layer implements it over TCP; tests script it.
/// The built-in controllers do not use it; it stays for diagnostics and for
/// comparing a device's description against OpenRGB's.
/// </summary>
public interface IOpenRgbClient
{
    Task<OperationResult<ILightingSession>> ConnectAsync(int port, CancellationToken cancellationToken = default);
}
