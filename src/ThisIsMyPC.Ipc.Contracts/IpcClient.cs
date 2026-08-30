using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Ipc.Contracts;

public interface IIpcClient
{
    Task<OperationResult<ServiceStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<DriftReportResponse>> GetDriftReportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Desktop-side pipe client (28-1). Each call opens a fresh connection with
/// <see cref="TokenImpersonationLevel.Identification"/> (SECURITY_SQOS_PRESENT |
/// SECURITY_IDENTIFICATION; a squatting server cannot impersonate us beyond
/// identification), sends one request with a fresh nonce, and requires the
/// response to echo it. A missing service degrades to ErrorCategory.ServiceUnavailable;
/// callers surface Owner Mode as unavailable, never as an error dialog.
/// </summary>
public sealed class IpcClient : IIpcClient
{
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public IpcClient(string? pipeName = null, TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName ?? IpcProtocol.PipeName;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2);
    }

    public Task<OperationResult<ServiceStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(IpcMessageTypes.ServiceStatus, IpcJsonContext.Default.ServiceStatusResponse, cancellationToken);

    public Task<OperationResult<DriftReportResponse>> GetDriftReportAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(IpcMessageTypes.DriftReport, IpcJsonContext.Default.DriftReportResponse, cancellationToken);

    private async Task<OperationResult<T>> RequestAsync<T>(
        string type,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> payloadTypeInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            try
            {
                await pipe.ConnectAsync(_connectTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                return OperationResult<T>.Failure(
                    "The ThisIsMyPC service is not running", ErrorCategory.ServiceUnavailable, ex);
            }

            var nonce = Guid.NewGuid().ToString("N");
            var request = new IpcEnvelope { Type = type, Nonce = nonce };
            await IpcProtocol.WriteFrameAsync(pipe, IpcSerializer.SerializeEnvelope(request), cancellationToken)
                .ConfigureAwait(false);

            var frame = await IpcProtocol.ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (frame is null)
                return OperationResult<T>.Failure(
                    "The service closed the connection without responding", ErrorCategory.ServiceUnavailable);

            var envelope = IpcSerializer.DeserializeEnvelope(frame);
            if (envelope is null)
                return OperationResult<T>.Failure(
                    "The service sent an unreadable response", ErrorCategory.ServiceUnavailable);

            if (!string.Equals(envelope.Nonce, nonce, StringComparison.Ordinal))
                return OperationResult<T>.Failure(
                    "Response nonce mismatch (possible replay); response discarded",
                    ErrorCategory.AccessDenied);

            if (envelope.Type == IpcMessageTypes.Error)
            {
                var error = envelope.PayloadJson is { } errorJson
                    ? JsonSerializer.Deserialize(errorJson, IpcJsonContext.Default.IpcErrorResponse)?.Message
                    : null;
                return OperationResult<T>.Failure(
                    error ?? "The service reported an error", ErrorCategory.ServiceUnavailable);
            }

            if (envelope.Type != type || envelope.PayloadJson is null)
                return OperationResult<T>.Failure(
                    $"Unexpected response type '{envelope.Type}'", ErrorCategory.ServiceUnavailable);

            var payload = JsonSerializer.Deserialize(envelope.PayloadJson, payloadTypeInfo);
            return payload is null
                ? OperationResult<T>.Failure("Empty response payload", ErrorCategory.ServiceUnavailable)
                : OperationResult<T>.Success(payload);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException
                                       or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return OperationResult<T>.Failure(
                $"IPC failure: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }
}
