using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Ipc;
using ThisIsMyPC.Interop.Win32.Security;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Broker;

internal static class Program
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(35);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
#if ACG_ENABLED
        if (!DynamicCodeHardening.Apply() || !DynamicCodeHardening.IsEnabled())
            Environment.FailFast("Arbitrary Code Guard could not be enabled.");
#endif
        if (!DllSearchHardening.Apply())
            Environment.FailFast("Safe DLL search policy could not be enabled.");

        if (!IsElevated())
            return Fail("The privilege broker must run as administrator.");
        if (!TryParseArguments(args, out var pipeName, out var expectedServerProcessId))
            return Fail("The privilege broker received invalid startup arguments.");

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.None);
            using var connect = new CancellationTokenSource(ConnectTimeout);
            await pipe.ConnectAsync(connect.Token).ConfigureAwait(false);

            var peer = PipePeerIdentity.GetServerProcessId(pipe);
            if (!peer.IsSuccess || peer.Value != expectedServerProcessId)
                return Fail(peer.ErrorMessage ?? "The callback pipe owner changed.");
            var trusted = BrokerProcessTrust.VerifyUiProcess(expectedServerProcessId);
            if (!trusted.IsSuccess)
                return Fail(trusted.ErrorMessage ?? "The ThisIsMyPC UI is not trusted.");
            var uiSid = ProcessTokenIdentity.GetUserSid(expectedServerProcessId);
            if (!uiSid.IsSuccess)
                return Fail(uiSid.ErrorMessage ?? "The ThisIsMyPC UI account is not trusted.");

            using var open = new CancellationTokenSource(ConnectTimeout);
            var opened = await OpenSession(pipe, open.Token).ConfigureAwait(false);
            if (!opened.IsSuccess)
                return Fail(opened.ErrorMessage ?? "The broker session was rejected.");

            await using var host = new DeferredModuleHost(uiSid.Value!);
            var ownerMode = new OwnerModeBrokerController(uiUserSid: uiSid.Value!);
            return await Serve(pipe, opened.Value!, host, ownerMode).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 2;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException
                                       or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail("The privilege broker stopped: " + ex.Message);
        }
    }

    // Control-only sessions must not depend on a healthy restoration journal or module graph.
    private sealed class DeferredModuleHost(string uiUserSid) : IAsyncDisposable
    {
        private PrivilegedModuleHost? _host;
        public PrivilegedModuleHost Value => _host ??= new(uiUserSid);
        public ValueTask DisposeAsync() => _host?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private static async Task<OperationResult<BrokerRequestPolicy>> OpenSession(
        NamedPipeClientStream pipe, CancellationToken cancellationToken, IpcEnvelope? provided = null)
    {
        var frame = provided is null
            ? await IpcProtocol.ReadFrameAsync(pipe, cancellationToken).ConfigureAwait(false)
            : null;
        var envelope = provided ?? (frame is null ? null : IpcSerializer.DeserializeEnvelope(frame));
        if (envelope is not { Type: IpcMessageTypes.BrokerSession, PayloadJson: not null })
            return OperationResult<BrokerRequestPolicy>.Failure("The session request is unreadable.", ErrorCategory.AccessDenied);

        BrokerSessionRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(envelope.PayloadJson, IpcJsonContext.Default.BrokerSessionRequest);
        }
        catch (JsonException ex)
        {
            await WriteSessionResponse(pipe, envelope.Nonce, false, "The session request is invalid.", cancellationToken)
                .ConfigureAwait(false);
            return OperationResult<BrokerRequestPolicy>.Failure("The session request is invalid.", ErrorCategory.AccessDenied, ex);
        }

        var policy = request is null
            ? OperationResult<BrokerRequestPolicy>.Failure("The session request is empty.", ErrorCategory.AccessDenied)
            : BrokerRequestPolicy.Create(request);
        if (!policy.IsSuccess)
        {
            await WriteSessionResponse(pipe, envelope.Nonce, false, policy.ErrorMessage, cancellationToken)
                .ConfigureAwait(false);
            return policy;
        }

        if (!policy.Value!.Persistent)
        {
            var decision = NativeReviewWindow.Show(policy.Value);
            if (decision != NativeReviewWindow.Decision.Apply)
            {
                var discarded = decision == NativeReviewWindow.Decision.Discarded
                    && policy.Value.HasBatchOperations;
                var cancelled = decision == NativeReviewWindow.Decision.Cancelled;
                var message = decision == NativeReviewWindow.Decision.Failed
                    ? "The administrator review could not open."
                    : discarded ? "Changes discarded." : "Administrator confirmation was cancelled.";
                await WriteSessionResponse(pipe, envelope.Nonce, false, message, cancellationToken,
                    discarded: discarded, cancelled: cancelled).ConfigureAwait(false);
                return OperationResult<BrokerRequestPolicy>.Failure(message,
                    discarded ? ErrorCategory.Discarded : cancelled ? ErrorCategory.Cancelled : ErrorCategory.ServiceUnavailable);
            }
        }

        await WriteSessionResponse(pipe, envelope.Nonce, true, null, cancellationToken).ConfigureAwait(false);
        return policy;
    }

    private static async Task<int> Serve(NamedPipeClientStream pipe, BrokerRequestPolicy policy,
        DeferredModuleHost host, OwnerModeBrokerController ownerMode)
    {
        var persistent = policy.Persistent;
        BrokerRequestPolicy? activePolicy = persistent ? null : policy;
        while (pipe.IsConnected)
        {
            using var idle = persistent ? new CancellationTokenSource() : new CancellationTokenSource(SessionIdleTimeout);
            var frame = await IpcProtocol.ReadFrameAsync(pipe, idle.Token).ConfigureAwait(false);
            if (frame is null)
                return 0;
            var envelope = IpcSerializer.DeserializeEnvelope(frame);
            if (envelope is null)
                return 3;
            if (envelope.Type == IpcMessageTypes.BrokerClose)
                return 0;
            if (persistent && envelope.Type == IpcMessageTypes.BrokerSession)
            {
                activePolicy = null;
                var reviewed = await OpenSession(pipe, idle.Token, envelope).ConfigureAwait(false);
                if (reviewed.IsSuccess && !reviewed.Value!.Persistent)
                    activePolicy = reviewed.Value;
                continue;
            }
            if (persistent && envelope.Type == IpcMessageTypes.BrokerEndBatch)
            {
                activePolicy = null;
                await WriteSessionResponse(pipe, envelope.Nonce, true, null, idle.Token,
                    IpcMessageTypes.BrokerEndBatch).ConfigureAwait(false);
                continue;
            }
            if (envelope is not { Type: IpcMessageTypes.BrokerCommand, PayloadJson: not null })
            {
                await WriteCommandResponse(pipe, envelope.Nonce, Failure("Unexpected broker message."), idle.Token)
                    .ConfigureAwait(false);
                continue;
            }

            BrokerCommandRequest? command;
            try
            {
                command = JsonSerializer.Deserialize(envelope.PayloadJson, IpcJsonContext.Default.BrokerCommandRequest);
            }
            catch (JsonException)
            {
                command = null;
            }
            using var operation = new CancellationTokenSource(OperationTimeout);
            var response = command is null || activePolicy is null || !activePolicy.Authorizes(command)
                ? Failure("The command was not approved for this broker session.")
                : await Execute(command, host, ownerMode, operation.Token).ConfigureAwait(false);
            await WriteCommandResponse(pipe, envelope.Nonce, response, operation.Token).ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task<BrokerCommandResponse> Execute(BrokerCommandRequest command,
        DeferredModuleHost host, OwnerModeBrokerController ownerMode, CancellationToken cancellationToken)
    {
        try
        {
            if (command.Kind == BrokerCommandKind.CreateRestorePoint)
            {
                var restorePoint = await host.Value.CreateRestorePoint(command.RestorePointDescription!).ConfigureAwait(false);
                return new()
                {
                    IsSuccess = restorePoint.IsSuccess,
                    ErrorMessage = restorePoint.Message,
                    ErrorCategory = restorePoint.IsSuccess ? null : ErrorCategory.ServiceUnavailable,
                    RestorePoint = restorePoint,
                };
            }

            OperationResult<bool> result = command.Kind switch
            {
                BrokerCommandKind.ApplyChange => await host.Value.Apply(command.Change!, revert: false, cancellationToken).ConfigureAwait(false),
                BrokerCommandKind.RevertChange => await host.Value.Apply(command.Change!, revert: true, cancellationToken).ConfigureAwait(false),
                BrokerCommandKind.ExecuteAction => await host.Value.Execute(command.Action!).ConfigureAwait(false),
                BrokerCommandKind.EnableOwnerMode => await ownerMode.Enable(cancellationToken).ConfigureAwait(false),
                BrokerCommandKind.DisableOwnerMode => await ownerMode.Disable(cancellationToken).ConfigureAwait(false),
                _ => OperationResult<bool>.Failure("Unknown broker command.", ErrorCategory.AccessDenied),
            };
            return new()
            {
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                ErrorCategory = result.ErrorCategory,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new()
            {
                IsSuccess = false,
                ErrorMessage = "The privileged operation failed: " + ex.Message,
                ErrorCategory = ErrorCategory.ServiceUnavailable,
            };
        }
    }

    private static Task WriteSessionResponse(Stream pipe, string nonce, bool accepted, string? error,
        CancellationToken cancellationToken, string type = IpcMessageTypes.BrokerSession,
        bool discarded = false, bool cancelled = false) => Write(pipe, new()
    {
        Type = type,
        Nonce = nonce,
        PayloadJson = JsonSerializer.Serialize(
            new BrokerSessionResponse { Accepted = accepted, Discarded = discarded, Cancelled = cancelled, ErrorMessage = error },
            IpcJsonContext.Default.BrokerSessionResponse),
    }, cancellationToken);

    private static Task WriteCommandResponse(Stream pipe, string nonce, BrokerCommandResponse response,
        CancellationToken cancellationToken) => Write(pipe, new()
    {
        Type = IpcMessageTypes.BrokerCommand,
        Nonce = nonce,
        PayloadJson = JsonSerializer.Serialize(response, IpcJsonContext.Default.BrokerCommandResponse),
    }, cancellationToken);

    private static Task Write(Stream pipe, IpcEnvelope envelope, CancellationToken cancellationToken) =>
        IpcProtocol.WriteFrameAsync(pipe, IpcSerializer.SerializeEnvelope(envelope), cancellationToken);

    private static BrokerCommandResponse Failure(string message) => new()
    {
        IsSuccess = false,
        ErrorMessage = message,
        ErrorCategory = ErrorCategory.AccessDenied,
    };

    private static bool TryParseArguments(string[] args, out string pipeName, out int serverProcessId)
    {
        pipeName = string.Empty;
        serverProcessId = 0;
        if (args.Length != 4 || args[0] != "--pipe" || args[2] != "--server-pid"
            || args[1].Length is < 20 or > 128 || !args[1].All(char.IsAsciiLetterOrDigit)
            || !int.TryParse(args[3], out serverProcessId) || serverProcessId <= 0)
        {
            return false;
        }
        pipeName = args[1];
        return true;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int Fail(string message)
    {
        NativeConfirmation.Error(message);
        return 1;
    }
}
