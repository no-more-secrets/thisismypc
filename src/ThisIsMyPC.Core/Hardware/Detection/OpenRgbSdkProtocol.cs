using System.Buffers.Binary;
using System.Text;

namespace ThisIsMyPC.Core.Hardware.Detection;

/// <summary>
/// The OpenRGB SDK server wire format, pure, limited to what the probe needs.
/// Packets are a 16-byte header (magic "ORGB", device index, packet id,
/// payload size, all little-endian 32-bit) followed by the payload. Source:
/// NetworkProtocol.h in the OpenRGB repository; the default port 6742 spells
/// ORGB on a phone keypad. Device data, colors and modes arrive with the
/// Lighting integration and its versioned parser.
/// </summary>
public static class OpenRgbSdkProtocol
{
    public const int DefaultPort = 6742;
    public const int HeaderLength = 16;

    public const uint RequestControllerCount = 0;
    public const uint SetClientName = 50;

    /// <summary>Sent by the server, unsolicited, whenever its device list changes; a probe skips it.</summary>
    public const uint DeviceListUpdated = 100;

    private static ReadOnlySpan<byte> Magic => "ORGB"u8;

    public readonly record struct PacketHeader(uint DeviceIndex, uint PacketId, uint PayloadLength);

    public static byte[] BuildPacket(uint deviceIndex, uint packetId, ReadOnlySpan<byte> payload)
    {
        var packet = new byte[HeaderLength + payload.Length];
        Magic.CopyTo(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), deviceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(packet.AsSpan(HeaderLength));
        return packet;
    }

    public static byte[] BuildControllerCountRequest() => BuildPacket(0, RequestControllerCount, []);

    /// <summary>Client name is a null-terminated string; the server never answers it.</summary>
    public static byte[] BuildSetClientName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var bytes = Encoding.UTF8.GetBytes(name + '\0');
        return BuildPacket(0, SetClientName, bytes);
    }

    public static bool TryParseHeader(ReadOnlySpan<byte> bytes, out PacketHeader header)
    {
        header = default;
        if (bytes.Length < HeaderLength || !bytes[..4].SequenceEqual(Magic))
            return false;

        header = new PacketHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4)));
        return true;
    }

    /// <summary>A four-byte little-endian value; anything else is malformed.</summary>
    public static bool TryParseUInt32Payload(ReadOnlySpan<byte> payload, out uint value)
    {
        value = 0;
        if (payload.Length != 4)
            return false;
        value = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        return true;
    }
}
