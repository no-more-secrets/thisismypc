using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Hardware.Lighting;

/// <summary>
/// The bundled lighting engine: OpenRGB's device core, built from the pinned
/// submodule without its GUI, running as a child process that serves the
/// OpenRGB SDK protocol on loopback. The engine is what makes every device
/// OpenRGB supports a ThisIsMyPC device; the app never needs OpenRGB installed.
/// </summary>
public interface ILightingEngine
{
    /// <summary>True when this build ships the engine binary.</summary>
    bool IsAvailable { get; }

    /// <summary>Starts the engine if it is not running and returns its private loopback endpoint.</summary>
    Task<OperationResult<LightingEngineEndpoint>> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks a running engine to detect devices again and waits for that pass to finish.</summary>
    Task<OperationResult<bool>> RescanAsync(CancellationToken cancellationToken = default);

    /// <summary>What the engine said while starting: refusals and its last log lines, for the tab's evidence.</summary>
    IReadOnlyList<string> Notes { get; }
}
