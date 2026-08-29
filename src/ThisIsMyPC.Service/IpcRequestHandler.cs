using System.Text.Json;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Service;

/// <summary>
/// Pure request → response mapping for the pipe server — separated from the
/// transport loop so the protocol is unit-testable without a real Session 0
/// service (28-1 AC). Every response echoes the request nonce.
/// </summary>
public sealed class IpcRequestHandler
{
    private readonly IDriftReportSource _driftSource;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly string _serviceVersion;

    public IpcRequestHandler(IDriftReportSource driftSource, DateTimeOffset startedAtUtc, string serviceVersion)
    {
        ArgumentNullException.ThrowIfNull(driftSource);
        _driftSource = driftSource;
        _startedAtUtc = startedAtUtc;
        _serviceVersion = serviceVersion;
    }

    public IpcEnvelope Handle(IpcEnvelope request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Type switch
        {
            IpcMessageTypes.Ping => new IpcEnvelope { Type = IpcMessageTypes.Ping, Nonce = request.Nonce },
            IpcMessageTypes.ServiceStatus => new IpcEnvelope
            {
                Type = IpcMessageTypes.ServiceStatus,
                Nonce = request.Nonce,
                PayloadJson = JsonSerializer.Serialize(BuildStatus(), IpcJsonContext.Default.ServiceStatusResponse),
            },
            IpcMessageTypes.DriftReport => new IpcEnvelope
            {
                Type = IpcMessageTypes.DriftReport,
                Nonce = request.Nonce,
                PayloadJson = JsonSerializer.Serialize(_driftSource.GetReport(), IpcJsonContext.Default.DriftReportResponse),
            },
            _ => MakeError(request.Nonce, $"Unknown message type '{request.Type}'"),
        };
    }

    public static IpcEnvelope MakeError(string nonce, string message) => new()
    {
        Type = IpcMessageTypes.Error,
        Nonce = nonce,
        PayloadJson = JsonSerializer.Serialize(
            new IpcErrorResponse { Message = message }, IpcJsonContext.Default.IpcErrorResponse),
    };

    private ServiceStatusResponse BuildStatus() => new()
    {
        ProtocolVersion = IpcProtocol.ProtocolVersion,
        ServiceVersion = _serviceVersion,
        StartedAtUtc = _startedAtUtc,
        BaselinePresent = _driftSource.BaselinePresent,
        LastDriftScanUtc = _driftSource.LastScanUtc,
    };
}
