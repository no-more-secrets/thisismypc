using System.IO.Pipes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Ipc.Contracts;
using ThisIsMyPC.Service;

namespace ThisIsMyPC.Ipc.Tests;

/// <summary>
/// 28-1 AC: a mock in-process pipe server validates client connection logic,
/// serialization, and error handling without a real Session 0 service. The mock
/// runs the production IpcRequestHandler behind a plain (un-hardened) pipe.
/// </summary>
public sealed class IpcClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static string UniquePipeName() => $"tipc-test-{Guid.NewGuid():N}";

    private sealed class FakeDriftSource : IDriftReportSource
    {
        public bool BaselinePresent => true;
        public DateTimeOffset? LastScanUtc => new DateTimeOffset(2026, 8, 29, 3, 0, 0, TimeSpan.Zero);

        public DriftReportResponse GetReport() => new()
        {
            BaselinePresent = true,
            GeneratedAtUtc = LastScanUtc,
            Items =
            [
                new DriftItem
                {
                    ModuleId = "Windows Annoyances",
                    SettingId = "copilot",
                    DisplayName = "Copilot button",
                    SystemLocation = @"HKCU\Software\...\ShowCopilotButton",
                    ValueType = "Registry_DWord",
                    ExpectedValue = "0",
                    CurrentValue = "1",
                    SuspectedCause = "Windows Update",
                },
            ],
        };
    }

    /// <summary>Serves exactly one client session using the production handler, then completes.</summary>
    private static async Task ServeOneSessionAsync(
        string pipeName, IpcRequestHandler handler, Func<IpcEnvelope, IpcEnvelope>? interceptor = null)
    {
        using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync().ConfigureAwait(false);

        while (true)
        {
            byte[]? frame;
            try
            {
                frame = await IpcProtocol.ReadFrameAsync(server, CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return;
            }
            if (frame is null)
                return;

            var request = IpcSerializer.DeserializeEnvelope(frame)!;
            var response = interceptor is not null ? interceptor(request) : handler.Handle(request);
            await IpcProtocol.WriteFrameAsync(
                server, IpcSerializer.SerializeEnvelope(response), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static IpcRequestHandler NewHandler() =>
        new(new FakeDriftSource(), new DateTimeOffset(2026, 8, 29, 2, 0, 0, TimeSpan.Zero), "1.2.3");

    [Fact]
    public async Task Status_round_trips_through_the_pipe()
    {
        var pipeName = UniquePipeName();
        var server = ServeOneSessionAsync(pipeName, NewHandler());
        var client = new IpcClient(pipeName, Timeout);

        var result = await client.GetStatusAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(IpcProtocol.ProtocolVersion, result.Value!.ProtocolVersion);
        Assert.Equal("1.2.3", result.Value.ServiceVersion);
        Assert.True(result.Value.BaselinePresent);
        await server;
    }

    [Fact]
    public async Task Drift_report_round_trips_with_items()
    {
        var pipeName = UniquePipeName();
        var server = ServeOneSessionAsync(pipeName, NewHandler());
        var client = new IpcClient(pipeName, Timeout);

        var result = await client.GetDriftReportAsync();

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("copilot", item.SettingId);
        Assert.Equal("Windows Update", item.SuspectedCause);
        Assert.Equal("0", item.ExpectedValue);
        Assert.Equal("1", item.CurrentValue);
        await server;
    }

    [Fact]
    public async Task No_server_degrades_to_service_unavailable()
    {
        var client = new IpcClient(UniquePipeName(), TimeSpan.FromMilliseconds(200));

        var result = await client.GetStatusAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
    }

    [Fact]
    public async Task Nonce_mismatch_is_rejected_as_replay()
    {
        var pipeName = UniquePipeName();
        var server = ServeOneSessionAsync(pipeName, NewHandler(),
            interceptor: request => NewHandler().Handle(request) with { Nonce = "stale-nonce" });
        var client = new IpcClient(pipeName, Timeout);

        var result = await client.GetStatusAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
        await server;
    }

    [Fact]
    public async Task Error_envelope_surfaces_the_service_message()
    {
        var pipeName = UniquePipeName();
        var server = ServeOneSessionAsync(pipeName, NewHandler(),
            interceptor: request => IpcRequestHandler.MakeError(request.Nonce, "baseline store corrupt"));
        var client = new IpcClient(pipeName, Timeout);

        var result = await client.GetDriftReportAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("baseline store corrupt", result.ErrorMessage);
        await server;
    }

    [Fact]
    public void Unknown_message_type_gets_an_error_envelope_echoing_the_nonce()
    {
        var response = NewHandler().Handle(new IpcEnvelope { Type = "pawnio-ioctl", Nonce = "n1" });

        Assert.Equal(IpcMessageTypes.Error, response.Type);
        Assert.Equal("n1", response.Nonce);
    }

    [Fact]
    public async Task Oversized_frame_length_is_rejected()
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xFF, 0xFF, 0x7F]); // ~2 GB claimed length
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => IpcProtocol.ReadFrameAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frame_round_trip_preserves_payload()
    {
        using var stream = new MemoryStream();
        var payload = IpcSerializer.SerializeEnvelope(new IpcEnvelope { Type = "ping", Nonce = "abc" });

        await IpcProtocol.WriteFrameAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;
        var read = await IpcProtocol.ReadFrameAsync(stream, CancellationToken.None);

        Assert.Equal(payload, read);
        Assert.Equal("abc", IpcSerializer.DeserializeEnvelope(read!)!.Nonce);
    }
}
