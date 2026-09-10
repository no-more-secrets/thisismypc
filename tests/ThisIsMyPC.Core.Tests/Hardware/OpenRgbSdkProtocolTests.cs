using System.Text;
using ThisIsMyPC.Core.Hardware.Detection;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class OpenRgbSdkProtocolTests
{
    [Fact]
    public void ControllerCountRequest_IsTheBareHeader()
    {
        var packet = OpenRgbSdkProtocol.BuildControllerCountRequest();

        Assert.Equal(16, packet.Length);
        Assert.Equal("ORGB", Encoding.ASCII.GetString(packet, 0, 4));
        Assert.True(OpenRgbSdkProtocol.TryParseHeader(packet, out var header));
        Assert.Equal(0u, header.DeviceIndex);
        Assert.Equal(OpenRgbSdkProtocol.RequestControllerCount, header.PacketId);
        Assert.Equal(0u, header.PayloadLength);
    }

    [Fact]
    public void SetClientName_CarriesANullTerminatedName()
    {
        var packet = OpenRgbSdkProtocol.BuildSetClientName("ThisIsMyPC");

        Assert.True(OpenRgbSdkProtocol.TryParseHeader(packet, out var header));
        Assert.Equal(OpenRgbSdkProtocol.SetClientName, header.PacketId);
        Assert.Equal(11u, header.PayloadLength);
        Assert.Equal(0, packet[^1]);
        Assert.Equal("ThisIsMyPC", Encoding.UTF8.GetString(packet, 16, 10));
    }

    [Fact]
    public void TryParseHeader_RejectsWrongMagicAndShortBuffers()
    {
        var packet = OpenRgbSdkProtocol.BuildControllerCountRequest();
        packet[0] = (byte)'X';

        Assert.False(OpenRgbSdkProtocol.TryParseHeader(packet, out _));
        Assert.False(OpenRgbSdkProtocol.TryParseHeader(new byte[15], out _));
    }

    [Fact]
    public void TryParseUInt32Payload_RequiresExactlyFourBytes()
    {
        Assert.True(OpenRgbSdkProtocol.TryParseUInt32Payload([2, 0, 0, 0], out var value));
        Assert.Equal(2u, value);
        Assert.False(OpenRgbSdkProtocol.TryParseUInt32Payload([2, 0, 0], out _));
        Assert.False(OpenRgbSdkProtocol.TryParseUInt32Payload([2, 0, 0, 0, 0], out _));
    }
}
