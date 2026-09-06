using System.IO.Pipes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class RestorationIpcBoundaryTests
{
    [Fact]
    public async Task OldStatusPayloadCannotClaimRestorationSupport()
    {
        var result = await WithReply(client => client.GetStatusAsync(), request => request with
        {
            PayloadJson = "{\"protocolVersion\":1,\"serviceVersion\":\"old\",\"startedAtUtc\":\"2026-01-01T00:00:00Z\",\"baselinePresent\":true}",
        });
        Assert.True(result.IsSuccess);
        Assert.Equal(RestorationServiceState.Unavailable, result.Value!.Restoration.State);
        Assert.False(result.Value.Restoration.ConsentGranted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ControlReplyWithWrongNonceIsRejected(bool enable)
    {
        var result = await WithReply(client => enable ? client.EnableRestorationAsync() : client.PauseRestorationAsync(),
            request => request with { Nonce = "wrong", PayloadJson = "{}" });
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Fact]
    public async Task EnableCannotAcceptStatusReplyInstead()
    {
        var result = await WithReply(client => client.EnableRestorationAsync(), request => request with
        { Type = IpcMessageTypes.ServiceStatus, PayloadJson = "{}" });
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task NullControlPayloadIsNotSuccess()
    {
        var result = await WithReply(client => client.EnableRestorationAsync(), request => request with { PayloadJson = "null" });
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ConnectedServerThatNeverRepliesTimesOut()
    {
        var name = "tipc-boundary-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(safety.Token);
        var request = new IpcClient(name, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(500)).EnableRestorationAsync(safety.Token);
        await connected;
        Assert.NotNull(await IpcProtocol.ReadFrameAsync(server, safety.Token));
        var result = await request;
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.False(safety.IsCancellationRequested);
    }

    [Theory]
    [InlineData(IpcMessageTypes.EnableRestoration)]
    [InlineData(IpcMessageTypes.PauseRestoration)]
    public async Task ControlStorageFailureReturnsErrorEnvelope(string type)
    {
        var handler = new ThisIsMyPC.Service.IpcRequestHandler(new EmptyDrift(), DateTimeOffset.UtcNow, "test", new ThrowingControl());
        var response = await handler.HandleAsync(new() { Type = type, Nonce = "test-nonce" });
        Assert.Equal(IpcMessageTypes.Error, response.Type);
        Assert.Equal("test-nonce", response.Nonce);
        Assert.Contains("storage unavailable", response.PayloadJson);
    }

    private sealed class ThrowingControl : ThisIsMyPC.Service.IRestorationServiceController
    {
        public RestorationStatusResponse GetStatus() => new();
        public Task<RestorationStatusResponse> EnableAsync(CancellationToken token = default) => throw new InvalidOperationException("storage unavailable");
        public Task<RestorationStatusResponse> PauseAsync(CancellationToken token = default) => throw new InvalidOperationException("storage unavailable");
    }
    private sealed class EmptyDrift : ThisIsMyPC.Service.IDriftReportSource
    {
        public bool BaselinePresent => false;
        public DateTimeOffset? LastScanUtc => null;
        public DriftReportResponse GetReport() => new() { BaselinePresent = false };
    }
    private static async Task<OperationResult<T>> WithReply<T>(Func<IpcClient, Task<OperationResult<T>>> operation,
        Func<IpcEnvelope, IpcEnvelope> reply)
    {
        var name = "tipc-boundary-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(safety.Token);
        var result = operation(new IpcClient(name, TimeSpan.FromSeconds(3)));
        await connected;
        var request = IpcSerializer.DeserializeEnvelope((await IpcProtocol.ReadFrameAsync(server, safety.Token))!)!;
        await IpcProtocol.WriteFrameAsync(server, IpcSerializer.SerializeEnvelope(reply(request)), safety.Token);
        return await result.WaitAsync(safety.Token);
    }
}
