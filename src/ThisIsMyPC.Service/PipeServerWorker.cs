using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Ipc;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Service;

/// <summary>
/// The 28-1 IPC server loop: one hardened single-instance pipe, one client session
/// at a time (the desktop app opens a fresh connection per request). Every response
/// echoes the request nonce; unknown types get an Error envelope. A squatted pipe
/// name is a hard security signal; logged and retried with backoff, never served
/// around.
/// </summary>
public sealed class PipeServerWorker : BackgroundService
{
    private static readonly TimeSpan SquatRetryDelay = TimeSpan.FromSeconds(30);
    // The pipe is single-instance: a client that connects and never sends would
    // otherwise wedge the server for everyone until service restart.
    private static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly IpcRequestHandler _handler;
    private readonly ILogger<PipeServerWorker> _logger;
    private readonly string _pipeName;

    public PipeServerWorker(
        IDriftReportSource driftSource, ILogger<PipeServerWorker> logger, string? pipeName = null)
    {
        _handler = new IpcRequestHandler(
            driftSource,
            DateTimeOffset.UtcNow,
            typeof(PipeServerWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0");
        _logger = logger;
        _pipeName = pipeName ?? IpcProtocol.PipeName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var created = HardenedPipeFactory.Create(_pipeName);
            if (!created.IsSuccess)
            {
                if (created.ErrorCategory == ErrorCategory.AccessDenied)
                    _logger.LogCritical("Pipe name appears squatted: {Error}", created.ErrorMessage);
                else
                    _logger.LogError("Pipe creation failed: {Error}", created.ErrorMessage);
                await Task.Delay(SquatRetryDelay, stoppingToken).ConfigureAwait(false);
                continue;
            }

            using var pipe = created.Value!;
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await ServeSessionAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
            {
                _logger.LogDebug(ex, "Client session ended abnormally");
            }
        }
    }

    private async Task ServeSessionAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        while (pipe.IsConnected && !token.IsCancellationRequested)
        {
            byte[]? frame;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                idle.CancelAfter(SessionIdleTimeout);
                try
                {
                    frame = await IpcProtocol.ReadFrameAsync(pipe, idle.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    _logger.LogDebug("Idle client session dropped after {Timeout}s", SessionIdleTimeout.TotalSeconds);
                    return;
                }
            }
            if (frame is null)
                return; // client hung up cleanly

            var request = IpcSerializer.DeserializeEnvelope(frame);
            var response = request is null
                ? IpcRequestHandler.MakeError(string.Empty, "Unreadable request")
                : _handler.Handle(request);

            await IpcProtocol.WriteFrameAsync(pipe, IpcSerializer.SerializeEnvelope(response), token)
                .ConfigureAwait(false);
        }
    }
}
