using System.Buffers.Binary;

namespace ThisIsMyPC.Ipc.Contracts;

/// <summary>
/// Wire-level constants and framing for the desktop-app / Session 0 service channel
/// (28-1). Frames are a 4-byte little-endian length prefix followed by a UTF-8 JSON
/// <see cref="IpcEnvelope"/>. The transport is a local-only named pipe; remote
/// clients are rejected at pipe-creation time and the server ACL admits only
/// Administrators and SYSTEM.
/// </summary>
public static class IpcProtocol
{
    public const string PipeName = "ThisIsMyPC";
    public const int ProtocolVersion = 1;

    /// <summary>Hard ceiling per frame; a drift report is small; anything bigger is hostile or corrupt.</summary>
    public const int MaxFrameBytes = 1024 * 1024;

    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > MaxFrameBytes)
            throw new InvalidOperationException($"Frame of {payload.Length} bytes exceeds the {MaxFrameBytes} byte limit");

        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one frame; null on clean end-of-stream before the prefix.</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prefix = new byte[4];
        if (!await TryReadExactAsync(stream, prefix, cancellationToken).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 0 or > MaxFrameBytes)
            throw new InvalidDataException($"Frame length {length} outside [0, {MaxFrameBytes}]");

        var payload = new byte[length];
        if (!await TryReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            throw new EndOfStreamException("Stream ended mid-frame");
        return payload;
    }

    private static async Task<bool> TryReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (chunk == 0)
                return read == 0 && buffer.Length > 0 ? false : throw new EndOfStreamException("Stream ended mid-frame");
            read += chunk;
        }
        return true;
    }
}
