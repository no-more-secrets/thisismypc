using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Lighting.Controllers.Sinowealth;
using ThisIsMyPC.Lighting.Tests.Fakes;
using ThisIsMyPC.Lighting.Transport;

namespace ThisIsMyPC.Lighting.Tests;

/// <summary>The Glorious Model O protocol against a scripted mouse: three collections, command on one, report 4 on another.</summary>
public class SinowealthControllerTests
{
    private const ushort Vid = 0x258A;
    private const ushort Pid = 0x0036;

    /// <summary>A scripted Model O: collection 1 is the command channel, collection 2 answers report 4, collection 0 answers nothing.</summary>
    private static (FakeHidTransport Transport, FakeHidDevice Command, FakeHidDevice Data, byte[] Configuration) Mouse()
    {
        var transport = new FakeHidTransport();
        var configuration = new byte[SinowealthController.ConfigReportSize];
        configuration[0] = 0x04;
        for (var i = 1; i < SinowealthController.ConfigSize; i++)
            configuration[i] = (byte)(0x10 + (i % 7));

        var infos = new[]
        {
            FakeHidTransport.Collection(@"\\?\hid#vid_258a&pid_0036&mi_01&col01", Vid, Pid, 0xFF00, 1, 1, 6),
            FakeHidTransport.Collection(@"\\?\hid#vid_258a&pid_0036&mi_01&col02", Vid, Pid, 0xFF00, 2, 1, 6),
            FakeHidTransport.Collection(@"\\?\hid#vid_258a&pid_0036&mi_01&col03", Vid, Pid, 0xFF00, 3, 1, 520),
            FakeHidTransport.Collection(@"\\?\hid#vid_258a&pid_0036&mi_00", Vid, Pid, 0x0001, 2, 0, 0),
        };
        transport.Collections.AddRange(infos);

        var silent = new FakeHidDevice(infos[0]);
        var command = new FakeHidDevice(infos[1])
        {
            OnSendFeature = report => report.Length == 6 && report[0] == 0x05 ? 6 : -1,
            OnGetFeature = buffer =>
            {
                // Firmware version query: [5, 0, ...] returns "V1.2".
                if (buffer.Length >= 6 && buffer[0] == 5)
                {
                    "V1.2"u8.CopyTo(buffer.AsSpan(2));
                    return 6;
                }
                return -1;
            },
        };
        var data = new FakeHidDevice(infos[2])
        {
            OnGetFeature = buffer =>
            {
                if (buffer.Length != SinowealthController.ConfigReportSize || buffer[0] != 0x04)
                    return -1;
                configuration.AsSpan().CopyTo(buffer);
                return SinowealthController.ConfigReportSize;
            },
            OnSendFeature = report => report.Length == SinowealthController.ConfigReportSize ? report.Length : -1,
        };
        transport.Devices[infos[0].Path] = silent;
        transport.Devices[infos[1].Path] = command;
        transport.Devices[infos[2].Path] = data;
        return (transport, command, data, configuration);
    }

    [Fact]
    public void Detect_FindsTheMouseOnce_AndClosesTheSpareCollection()
    {
        var (transport, command, data, _) = Mouse();
        var backend = new NativeLightingBackend(transport, i2c: null);

        var (entries, inventory) = backend.Detect();

        var device = Assert.Single(inventory.Devices);
        Assert.Equal("Glorious Model O / O-", device.Name);
        Assert.Equal(LightingDeviceType.Mouse, device.Type);
        Assert.Equal("Sinowealth", device.Controller);
        Assert.Single(entries);
        // The silent collection was opened for the probe and released; the two in use stay open.
        Assert.Equal(1, transport.Devices[transport.Collections[0].Path].DisposeCount);
        Assert.Equal(0, command.DisposeCount);
        Assert.Equal(0, data.DisposeCount);
        Assert.Contains(inventory.Notes, n => n.StartsWith("Glorious Model O / O-: found at", StringComparison.Ordinal));
    }

    [Fact]
    public void Describe_ListsTheTenModes_WithFirmwareAndSerial()
    {
        var (transport, _, _, _) = Mouse();
        var (entries, _) = new NativeLightingBackend(transport, i2c: null).Detect();
        var device = entries[0].Controller.Describe(0);

        Assert.Equal(10, device.Modes.Count);
        Assert.Equal("Static", device.Modes[0].Name);
        Assert.True(device.Modes[0].HasPerLedColor);
        Assert.True(device.Modes[0].HasBrightness);
        Assert.Equal((1u, 4u), device.Modes[0].BrightnessRange);
        Assert.Equal("Rainbow", device.Modes[2].Name);
        Assert.Contains(LightingDirection.Up, device.Modes[2].Directions);
        Assert.Equal(7, device.Modes[3].Colors.Count);
        Assert.Equal("V1.2", device.Version);
        Assert.Equal("SN-1", device.Serial);
        Assert.Single(device.Leds);
        Assert.All(device.Modes, m => Assert.True(m.Flags.HasFlag(LightingModeFlags.AutomaticSave)));
    }

    [Fact]
    public void SetLeds_InStaticMode_WritesTheColorAsRedBlueGreen_OverTheReadConfiguration()
    {
        var (transport, _, data, configuration) = Mouse();
        var (entries, _) = new NativeLightingBackend(transport, i2c: null).Detect();
        var controller = entries[0].Controller;

        var result = controller.SetLeds([new RgbColor(0x11, 0x22, 0x33)]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var report = Assert.Single(data.SentFeatureReports);
        Assert.Equal(SinowealthController.ConfigReportSize, report.Length);
        Assert.Equal(0x7B, report[0x03]);
        Assert.Equal(0x00, report[0x06]);
        Assert.Equal(0x02, report[0x35]);
        Assert.Equal(0x20, report[0x38]);
        Assert.Equal(0x11, report[0x39]);
        Assert.Equal(0x33, report[0x3A]);
        Assert.Equal(0x22, report[0x3B]);
        // Untouched bytes come from the configuration the mouse returned.
        Assert.Equal(configuration[0x10], report[0x10]);
        Assert.Equal(configuration[0xA0], report[0xA0]);
        // Nothing past the 167 copied bytes.
        Assert.Equal(0, report[0xA7]);
    }

    [Fact]
    public void SetMode_Rainbow_PacksBrightnessAndSpeed_AndDirection()
    {
        var (transport, _, data, _) = Mouse();
        var (entries, _) = new NativeLightingBackend(transport, i2c: null).Detect();
        var controller = entries[0].Controller;
        var rainbow = controller.Describe(0).Modes[2] with { Speed = 3, Direction = LightingDirection.Down };

        Assert.True(controller.SetMode(rainbow).IsSuccess);

        var report = Assert.Single(data.SentFeatureReports);
        Assert.Equal(0x01, report[0x35]);
        // No brightness on this mode: high (4) is used; speed 3.
        Assert.Equal(0x43, report[0x36]);
        Assert.Equal(0x00, report[0x37]);
        Assert.Equal(2, controller.Describe(0).ActiveModeIndex);
        Assert.Equal(3u, controller.Describe(0).Modes[2].Speed);
    }

    [Fact]
    public void SetMode_Rave_WritesTwoModeColors()
    {
        var (transport, _, data, _) = Mouse();
        var (entries, _) = new NativeLightingBackend(transport, i2c: null).Detect();
        var controller = entries[0].Controller;
        var rave = controller.Describe(0).Modes[6] with
        {
            Speed = 1, Brightness = 2,
            Colors = [new RgbColor(1, 2, 3), new RgbColor(4, 5, 6)],
        };

        Assert.True(controller.SetMode(rave).IsSuccess);

        var report = Assert.Single(data.SentFeatureReports);
        Assert.Equal(0x07, report[0x35]);
        Assert.Equal(0x21, report[0x74]);
        Assert.Equal(new byte[] { 1, 3, 2, 4, 6, 5 }, report[0x75..0x7B]);
    }

    [Fact]
    public void SetMode_WhenTheMouseStopsAnswering_Fails()
    {
        var (transport, _, data, _) = Mouse();
        var (entries, _) = new NativeLightingBackend(transport, i2c: null).Detect();
        data.OnGetFeature = _ => -1;

        var result = entries[0].Controller.SetMode(entries[0].Controller.Describe(0).Modes[1]);

        Assert.False(result.IsSuccess);
        Assert.Empty(data.SentFeatureReports);
    }

    [Fact]
    public void Detect_SkipsAMouse_WhoseCollectionsAreIncomplete()
    {
        var (transport, _, _, _) = Mouse();
        transport.Collections.RemoveAt(0);

        var (entries, inventory) = new NativeLightingBackend(transport, i2c: null).Detect();

        Assert.Empty(entries);
        Assert.Empty(inventory.Devices);
        Assert.Empty(transport.Opened);
    }

    [Fact]
    public async Task Backend_Session_ReadsAndWritesThroughTheController()
    {
        var (transport, _, data, _) = Mouse();
        using var backend = new NativeLightingBackend(transport, i2c: null);

        var opened = await backend.OpenAsync();
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        using var session = opened.Value!;
        var devices = await session.GetDevicesAsync();
        Assert.True(devices.IsSuccess);
        var mouse = Assert.Single(devices.Value!);
        Assert.Equal(0, mouse.Index);

        var written = await session.SetLedsAsync(0, [new RgbColor(9, 8, 7)]);
        Assert.True(written.IsSuccess, written.ErrorMessage);
        Assert.Single(data.SentFeatureReports);

        var missing = await session.SetLedsAsync(5, [new RgbColor(9, 8, 7)]);
        Assert.False(missing.IsSuccess);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Backend_Rescan_RaisesDeviceListChanged_AndDisposesOldControllers()
    {
        var (transport, command, _, _) = Mouse();
        using var backend = new NativeLightingBackend(transport, i2c: null);
        using var session = (await backend.OpenAsync()).Value!;
        var changes = 0;
        session.DeviceListChanged += (_, _) => changes++;

        var rescan = await backend.DetectAsync(rescan: true);

        Assert.True(rescan.IsSuccess);
        Assert.Equal(1, changes);
        Assert.Equal(1, command.DisposeCount);
        Assert.Equal(2, command.OpenCount);
    }
}
