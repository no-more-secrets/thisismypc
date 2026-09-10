using System.Net.Sockets;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Hardware;

/// <summary>
/// TCP client for the OpenRGB SDK server on localhost. One request at a time
/// per session; replies are matched by packet id, and the server's
/// unsolicited packets (its name, device-list notifications) are consumed
/// on the way. Every failure comes back as a result, never an exception.
/// </summary>
public sealed class OpenRgbSdkClient : IOpenRgbClient
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.OpenRgb");
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DeviceReplyTimeout = TimeSpan.FromSeconds(4);
    private const int MaxPayload = 8 * 1024 * 1024;

    public async Task<OperationResult<ILightingSession>> ConnectAsync(int port, CancellationToken cancellationToken = default)
    {
        var client = new TcpClient();
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(ConnectTimeout);
            await client.ConnectAsync("127.0.0.1", port, connectTimeout.Token).ConfigureAwait(false);

            var session = new Session(client);
            var handshake = await session.HandshakeAsync(cancellationToken).ConfigureAwait(false);
            if (!handshake.IsSuccess)
            {
                session.Dispose();
                return OperationResult<ILightingSession>.Failure(handshake.ErrorMessage!, handshake.ErrorCategory ?? ErrorCategory.ServiceUnavailable, handshake.Exception);
            }
            return OperationResult<ILightingSession>.Success(session);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            return OperationResult<ILightingSession>.Failure($"The lighting service did not accept a connection on port {port}.", ErrorCategory.ServiceUnavailable);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            client.Dispose();
            return OperationResult<ILightingSession>.Failure($"The lighting service is not answering on port {port}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private sealed class Session : ILightingSession
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _io = new(1, 1);
        private bool _broken;

        public Session(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public uint ProtocolVersion { get; private set; }

        public bool IsConnected => !_broken && _client.Connected;

        public event EventHandler? DeviceListChanged;

        /// <summary>
        /// Version negotiation as NetworkClient.cpp does it: ask with our
        /// version, take the lower of the two, fall back to 0 when the server
        /// never answers (a server too old to know the request).
        /// </summary>
        public async Task<OperationResult<bool>> HandshakeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _stream.WriteAsync(OpenRgbSdkProtocol.BuildProtocolVersionRequest(OpenRgbSdkProtocol.ClientProtocolVersion), cancellationToken).ConfigureAwait(false);
                uint serverVersion = 0;
                using (var versionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    versionTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        var reply = await ReadUntilAsync(OpenRgbSdkProtocol.RequestProtocolVersion, versionTimeout.Token).ConfigureAwait(false);
                        if (!OpenRgbSdkProtocol.TryParseUInt32Payload(reply.Payload, out serverVersion))
                            return OperationResult<bool>.Failure("The lighting service sent a malformed protocol version.", ErrorCategory.ServiceUnavailable);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        serverVersion = 0;
                    }
                }
                ProtocolVersion = Math.Min(serverVersion, OpenRgbSdkProtocol.ClientProtocolVersion);
                await _stream.WriteAsync(OpenRgbSdkProtocol.BuildSetClientName("ThisIsMyPC"), cancellationToken).ConfigureAwait(false);
                Log.Info("OpenRGB SDK session open (server protocol {Server}, using {Used})", serverVersion, ProtocolVersion);
                return OperationResult<bool>.Success(true);
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
            {
                _broken = true;
                return OperationResult<bool>.Failure($"The lighting service closed the connection during setup: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
        }

        /// <summary>
        /// Count, then one description per index. A server never answers an
        /// index that vanished between the two (a rescan mid-way), so a device
        /// that stays silent ends the list early and the caller is told the
        /// list changed instead of the session being written off.
        /// </summary>
        public async Task<OperationResult<IReadOnlyList<LightingDevice>>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            var listChanged = false;
            var result = await RunAsync(async ct =>
            {
                await _stream.WriteAsync(OpenRgbSdkProtocol.BuildControllerCountRequest(), ct).ConfigureAwait(false);
                var countReply = await ReadUntilAsync(OpenRgbSdkProtocol.RequestControllerCount, ct).ConfigureAwait(false);
                if (!OpenRgbSdkProtocol.TryParseUInt32Payload(countReply.Payload, out var count) || count > 256)
                    throw new InvalidDataException("The controller count reply was malformed.");

                var devices = new List<LightingDevice>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var device = await ReadDeviceAsync(i, ct).ConfigureAwait(false);
                    if (device is null)
                    {
                        listChanged = true;
                        break;
                    }
                    devices.Add(device);
                }
                return (IReadOnlyList<LightingDevice>)devices;
            }, cancellationToken).ConfigureAwait(false);

            if (listChanged)
                DeviceListChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }

        public Task<OperationResult<LightingDevice>> GetDeviceAsync(int deviceIndex, CancellationToken cancellationToken = default) =>
            RunAsync(async ct =>
                await ReadDeviceAsync((uint)deviceIndex, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"The lighting service no longer lists device {deviceIndex}."),
                cancellationToken);

        /// <summary>Null when the server sent nothing for this index within its own timeout.</summary>
        private async Task<LightingDevice?> ReadDeviceAsync(uint index, CancellationToken ct)
        {
            await _stream.WriteAsync(OpenRgbSdkProtocol.BuildControllerDataRequest(index, ProtocolVersion), ct).ConfigureAwait(false);
            using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            replyTimeout.CancelAfter(DeviceReplyTimeout);
            try
            {
                var reply = await ReadUntilAsync(OpenRgbSdkProtocol.RequestControllerData, replyTimeout.Token).ConfigureAwait(false);
                return LightingWire.ParseDevice(reply.Payload, ProtocolVersion, (int)index);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Log.Warn("OpenRGB sent no description for device {Index}; treating the list as changed", index);
                return null;
            }
        }

        public Task<OperationResult<bool>> SetModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default) =>
            SendAsync(LightingWire.BuildUpdateMode((uint)deviceIndex, mode, ProtocolVersion), cancellationToken);

        public Task<OperationResult<bool>> SetLedsAsync(int deviceIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default) =>
            SendAsync(LightingWire.BuildUpdateLeds((uint)deviceIndex, colors), cancellationToken);

        public Task<OperationResult<bool>> SetZoneLedsAsync(int deviceIndex, int zoneIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default) =>
            SendAsync(LightingWire.BuildUpdateZoneLeds((uint)deviceIndex, zoneIndex, colors), cancellationToken);

        public Task<OperationResult<bool>> SaveModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default) =>
            SendAsync(LightingWire.BuildUpdateMode((uint)deviceIndex, mode, ProtocolVersion, save: true), cancellationToken);

        private Task<OperationResult<bool>> SendAsync(byte[] packet, CancellationToken cancellationToken) =>
            RunAsync(async ct =>
            {
                await _stream.WriteAsync(packet, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);

        /// <summary>One operation at a time on the socket; a socket error marks the session broken for good.</summary>
        private async Task<OperationResult<T>> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            if (_broken)
                return OperationResult<T>.Failure("The lighting service connection was lost. Refresh the page to reconnect.", ErrorCategory.ServiceUnavailable);

            await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ReplyTimeout);
                return OperationResult<T>.Success(await operation(timeout.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _broken = true;
                return OperationResult<T>.Failure("The lighting service stopped answering.", ErrorCategory.ServiceUnavailable);
            }
            catch (InvalidDataException ex)
            {
                return OperationResult<T>.Failure($"The lighting service sent data this app could not read: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
            {
                _broken = true;
                return OperationResult<T>.Failure($"The lighting service connection was lost: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
            }
            finally
            {
                _io.Release();
            }
        }

        private readonly record struct Packet(OpenRgbSdkProtocol.PacketHeader Header, byte[] Payload);

        /// <summary>Reads packets until one carries <paramref name="packetId"/>; notifications and greetings on the way are handled or dropped.</summary>
        private async Task<Packet> ReadUntilAsync(uint packetId, CancellationToken ct)
        {
            var header = new byte[OpenRgbSdkProtocol.HeaderLength];
            while (true)
            {
                await _stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
                if (!OpenRgbSdkProtocol.TryParseHeader(header, out var parsed))
                    throw new InvalidDataException("A packet did not start with the OpenRGB magic.");
                if (parsed.PayloadLength > MaxPayload)
                    throw new InvalidDataException($"A packet declared {parsed.PayloadLength} bytes.");

                var payload = new byte[parsed.PayloadLength];
                if (payload.Length > 0)
                    await _stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);

                if (parsed.PacketId == packetId)
                    return new Packet(parsed, payload);
                if (parsed.PacketId == OpenRgbSdkProtocol.DeviceListUpdated)
                    DeviceListChanged?.Invoke(this, EventArgs.Empty);
                // Anything else (server name, later-version greetings) is not for us.
            }
        }

        public void Dispose()
        {
            _broken = true;
            _stream.Dispose();
            _client.Dispose();
            _io.Dispose();
        }
    }
}
