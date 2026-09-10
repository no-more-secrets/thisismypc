using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Ipc.Contracts;

public interface IIpcClient
{
    Task<OperationResult<RestorationHistoryResponse>> GetRestorationHistoryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RestorationHistoryResponse>.Failure("Owner Mode history is unavailable.", ErrorCategory.ServiceUnavailable));
    Task<OperationResult<ServiceStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<DriftReportResponse>> GetDriftReportAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<RestorationStatusResponse>> EnableRestorationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RestorationStatusResponse>.Failure("Restoration control is unavailable.", ErrorCategory.ServiceUnavailable));
    Task<OperationResult<RestorationStatusResponse>> PauseRestorationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<RestorationStatusResponse>.Failure("Restoration control is unavailable.", ErrorCategory.ServiceUnavailable));
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
    private readonly TimeSpan _requestTimeout;

    public IpcClient(string? pipeName = null, TimeSpan? connectTimeout = null, TimeSpan? requestTimeout = null)
    {
        _pipeName = pipeName ?? IpcProtocol.PipeName;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(2);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(35);
    }

    public async Task<OperationResult<ServiceStatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequestAsync(IpcMessageTypes.ServiceStatus, IpcJsonContext.Default.ServiceStatusResponse, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value is { Restoration: null } status
            ? OperationResult<ServiceStatusResponse>.Success(status with { Restoration = new() }) : result;
    }

    public Task<OperationResult<DriftReportResponse>> GetDriftReportAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(IpcMessageTypes.DriftReport, IpcJsonContext.Default.DriftReportResponse, cancellationToken);

    public Task<OperationResult<RestorationHistoryResponse>> GetRestorationHistoryAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(IpcMessageTypes.RestorationHistory, IpcJsonContext.Default.RestorationHistoryResponse, cancellationToken);

    public Task<OperationResult<RestorationStatusResponse>> EnableRestorationAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(IpcMessageTypes.EnableRestoration, IpcJsonContext.Default.RestorationStatusResponse, cancellationToken);

    public Task<OperationResult<RestorationStatusResponse>> PauseRestorationAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(IpcMessageTypes.PauseRestoration, IpcJsonContext.Default.RestorationStatusResponse, cancellationToken);

    private async Task<OperationResult<T>> RequestAsync<T>(
        string type,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> payloadTypeInfo,
        CancellationToken cancellationToken)
    {
        var callerToken = cancellationToken;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(_requestTimeout);
        cancellationToken = timeout.Token;
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
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            return OperationResult<T>.Failure("The service request timed out.", ErrorCategory.ServiceUnavailable);
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
