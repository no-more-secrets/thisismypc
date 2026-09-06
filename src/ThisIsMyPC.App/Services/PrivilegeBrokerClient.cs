using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Actions;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Ipc;
using ThisIsMyPC.Interop.Win32.Security;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.App.Services;

public interface IPrivilegeBrokerSession : IAsyncDisposable
{
    Task<OperationResult<bool>> ApplyChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> RevertChangeAsync(ChangeDescriptor change, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> ExecuteActionAsync(ActionDescriptor action, CancellationToken cancellationToken = default);
    Task<RestorePointResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> EnableOwnerModeAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> DisableOwnerModeAsync(CancellationToken cancellationToken = default);
}

public interface IPrivilegeBrokerClient
{
    Task<OperationResult<IPrivilegeBrokerSession>> OpenSessionAsync(
        BrokerSessionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts the signed elevated broker after creating a random callback pipe.
/// Both ends bind the pipe to the exact process identifiers they opened.
/// </summary>
public sealed class PrivilegeBrokerClient : IPrivilegeBrokerClient
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);
    private readonly string _brokerPath;

    public PrivilegeBrokerClient(string? brokerPath = null)
    {
        _brokerPath = brokerPath ?? Path.Combine(AppContext.BaseDirectory, "ThisIsMyPC.Broker.exe");
    }

    public async Task<OperationResult<IPrivilegeBrokerSession>> OpenSessionAsync(
        BrokerSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(_brokerPath))
        {
            return OperationResult<IPrivilegeBrokerSession>.Failure(
                "The ThisIsMyPC privilege broker is missing. Reinstall ThisIsMyPC.",
                ErrorCategory.NotFound);
        }

#if !DEBUG
        var trust = AuthenticodeVerifier.VerifyTrusted(_brokerPath, AppConstants.PublisherName, exactSignerName: true);
        if (!trust.IsSuccess)
        {
            return OperationResult<IPrivilegeBrokerSession>.Failure(
                trust.ErrorMessage ?? "The privilege broker signature is invalid.",
                ErrorCategory.AccessDenied, trust.Exception);
        }
#endif

        var userSid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(userSid))
        {
            return OperationResult<IPrivilegeBrokerSession>.Failure(
                "The signed-in Windows account SID is unavailable.", ErrorCategory.AccessDenied);
        }

        var pipeName = "ThisIsMyPCBroker" + Guid.NewGuid().ToString("N");
        var created = HardenedPipeFactory.CreateForUser(pipeName, userSid);
        if (!created.IsSuccess)
        {
            return OperationResult<IPrivilegeBrokerSession>.Failure(
                created.ErrorMessage ?? "The broker callback pipe could not be created.",
                created.ErrorCategory ?? ErrorCategory.ServiceUnavailable,
                created.Exception);
        }

        var pipe = created.Value!;
        Process? process = null;
        try
        {
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = _brokerPath,
                    Arguments = $"--pipe {pipeName} --server-pid {Environment.ProcessId}",
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = Environment.SystemDirectory,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return OperationResult<IPrivilegeBrokerSession>.Failure(
                    "Administrator confirmation was cancelled.", ErrorCategory.AccessDenied, ex);
            }

            if (process is null)
                return Failure("Windows did not start the privilege broker.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartTimeout);
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);

            var peer = PipePeerIdentity.GetClientProcessId(pipe);
            if (!peer.IsSuccess || peer.Value != process.Id)
            {
                return Failure(peer.ErrorMessage ?? "The callback pipe connected to the wrong process.");
            }

            var session = new PrivilegeBrokerSession(pipe, process);
            pipe = null!;
            process = null;
            var opened = await session.Open(request, cancellationToken).ConfigureAwait(false);
            if (!opened.IsSuccess)
            {
                await session.DisposeAsync().ConfigureAwait(false);
                return OperationResult<IPrivilegeBrokerSession>.Failure(
                    opened.ErrorMessage ?? "The privilege broker rejected the session.",
                    opened.ErrorCategory ?? ErrorCategory.AccessDenied,
                    opened.Exception);
            }
            return OperationResult<IPrivilegeBrokerSession>.Success(session);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("The privilege broker did not connect in time.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException
                                       or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return OperationResult<IPrivilegeBrokerSession>.Failure(
                "The privilege broker could not start: " + ex.Message,
                ErrorCategory.ServiceUnavailable, ex);
        }
        finally
        {
            pipe?.Dispose();
            process?.Dispose();
        }
    }

    private static OperationResult<IPrivilegeBrokerSession> Failure(string message) =>
        OperationResult<IPrivilegeBrokerSession>.Failure(message, ErrorCategory.ServiceUnavailable);

    private sealed class PrivilegeBrokerSession : IPrivilegeBrokerSession
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly Process _process;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _disposed;

        internal PrivilegeBrokerSession(NamedPipeServerStream pipe, Process process)
        {
            _pipe = pipe;
            _process = process;
        }

        internal async Task<OperationResult<bool>> Open(
            BrokerSessionRequest request, CancellationToken cancellationToken)
        {
            var response = await Exchange(
                IpcMessageTypes.BrokerSession,
                JsonSerializer.Serialize(request, IpcJsonContext.Default.BrokerSessionRequest),
                IpcJsonContext.Default.BrokerSessionResponse,
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccess && response.Value!.Accepted
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure(
                    response.Value?.ErrorMessage ?? response.ErrorMessage ?? "The broker session was rejected.",
                    ErrorCategory.AccessDenied);
        }

        public Task<OperationResult<bool>> ApplyChangeAsync(
            ChangeDescriptor change, CancellationToken cancellationToken = default) =>
            Run(new() { Kind = BrokerCommandKind.ApplyChange, Change = change }, cancellationToken);

        public Task<OperationResult<bool>> RevertChangeAsync(
            ChangeDescriptor change, CancellationToken cancellationToken = default) =>
            Run(new() { Kind = BrokerCommandKind.RevertChange, Change = change }, cancellationToken);

        public Task<OperationResult<bool>> ExecuteActionAsync(
            ActionDescriptor action, CancellationToken cancellationToken = default) =>
            Run(new() { Kind = BrokerCommandKind.ExecuteAction, Action = action }, cancellationToken);

        public async Task<RestorePointResult> CreateRestorePointAsync(
            string description, CancellationToken cancellationToken = default)
        {
            var response = await RunResponse(new()
            {
                Kind = BrokerCommandKind.CreateRestorePoint,
                RestorePointDescription = description,
            }, cancellationToken).ConfigureAwait(false);
            return response.RestorePoint ?? new()
            {
                Outcome = RestorePointOutcome.Failed,
                Message = response.ErrorMessage ?? "The privilege broker did not return a restore point result.",
            };
        }

        public Task<OperationResult<bool>> EnableOwnerModeAsync(CancellationToken cancellationToken = default) =>
            Run(new() { Kind = BrokerCommandKind.EnableOwnerMode }, cancellationToken);

        public Task<OperationResult<bool>> DisableOwnerModeAsync(CancellationToken cancellationToken = default) =>
            Run(new() { Kind = BrokerCommandKind.DisableOwnerMode }, cancellationToken);

        private async Task<OperationResult<bool>> Run(
            BrokerCommandRequest request, CancellationToken cancellationToken)
        {
            var response = await RunResponse(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccess
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure(
                    response.ErrorMessage ?? "The privileged operation failed.",
                    response.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
        }

        private async Task<BrokerCommandResponse> RunResponse(
            BrokerCommandRequest request, CancellationToken cancellationToken)
        {
            var result = await Exchange(
                IpcMessageTypes.BrokerCommand,
                JsonSerializer.Serialize(request, IpcJsonContext.Default.BrokerCommandRequest),
                IpcJsonContext.Default.BrokerCommandResponse,
                cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? result.Value! : new()
            {
                IsSuccess = false,
                ErrorMessage = result.ErrorMessage,
                ErrorCategory = result.ErrorCategory,
            };
        }

        private async Task<OperationResult<T>> Exchange<T>(string type, string payload,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> responseType,
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var nonce = Guid.NewGuid().ToString("N");
                var request = new IpcEnvelope { Type = type, Nonce = nonce, PayloadJson = payload };
                await IpcProtocol.WriteFrameAsync(
                    _pipe, IpcSerializer.SerializeEnvelope(request), cancellationToken).ConfigureAwait(false);
                var frame = await IpcProtocol.ReadFrameAsync(_pipe, cancellationToken).ConfigureAwait(false);
                var response = frame is null ? null : IpcSerializer.DeserializeEnvelope(frame);
                if (response is null || response.Type != type || response.Nonce != nonce || response.PayloadJson is null)
                {
                    return OperationResult<T>.Failure(
                        "The privilege broker returned an invalid response.", ErrorCategory.AccessDenied);
                }
                var value = JsonSerializer.Deserialize(response.PayloadJson, responseType);
                return value is null
                    ? OperationResult<T>.Failure("The privilege broker returned an empty response.", ErrorCategory.AccessDenied)
                    : OperationResult<T>.Success(value);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or JsonException)
            {
                return OperationResult<T>.Failure(
                    "The privilege broker connection failed: " + ex.Message,
                    ErrorCategory.ServiceUnavailable, ex);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (_pipe.IsConnected)
                {
                    var close = new IpcEnvelope
                    {
                        Type = IpcMessageTypes.BrokerClose,
                        Nonce = Guid.NewGuid().ToString("N"),
                    };
                    await IpcProtocol.WriteFrameAsync(
                        _pipe, IpcSerializer.SerializeEnvelope(close), CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                // The broker already exited.
            }
            finally
            {
                _pipe.Dispose();
                _process.Dispose();
                _gate.Release();
                _gate.Dispose();
            }
        }
    }
}
