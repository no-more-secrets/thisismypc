using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Interop.Win32.Hardware;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests.Hardware;

/// <summary>
/// Opt-in: writes to real lighting. Set TIPC_LIGHTING_WRITE=1 to run it.
/// Picks the first device with a per-LED mode, switches it to that mode,
/// paints every LED one color for two seconds, then puts the original mode
/// back with its original settings. Nothing is saved to the device.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class OpenRgbLiveWriteTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PerLedColor_WritesAndRestores()
    {
        if (Environment.GetEnvironmentVariable("TIPC_LIGHTING_WRITE") != "1")
        {
            output.WriteLine("Skipped: set TIPC_LIGHTING_WRITE=1 to write to real lighting.");
            return;
        }

        var environment = new Win32HardwareProbeEnvironment();
        var config = Path.Combine(DiagnosticsRoot(), "openrgb-live", Guid.NewGuid().ToString("N"));
        using var host = new BundledOpenRgbHost(environment, config);
        var start = await host.EnsureRunningAsync();
        Assert.True(start.IsSuccess, start.ErrorMessage);

        var connect = await new OpenRgbSdkClient().ConnectAsync(host.Port);
        Assert.True(connect.IsSuccess, connect.ErrorMessage);
        using var session = connect.Value!;
        var devices = await session.GetDevicesAsync();
        Assert.True(devices.IsSuccess, devices.ErrorMessage);

        var device = devices.Value!.FirstOrDefault(d => d.Modes.Any(m => m.HasPerLedColor) && d.Leds.Count > 0);
        Assert.NotNull(device);
        var original = device!.ActiveMode ?? device.Modes[0];
        var perLed = device.Modes.First(m => m.HasPerLedColor);
        output.WriteLine($"Device {device.Name}: active '{original.Name}', painting through '{perLed.Name}' ({device.Leds.Count} LEDs)");

        var setMode = await session.SetModeAsync(device.Index, perLed with { ColorMode = LightingColorMode.PerLed });
        Assert.True(setMode.IsSuccess, setMode.ErrorMessage);
        var paint = await session.SetLedsAsync(device.Index, Enumerable.Repeat(new RgbColor(255, 0, 0), device.Leds.Count).ToList());
        Assert.True(paint.IsSuccess, paint.ErrorMessage);

        var readBack = await session.GetDeviceAsync(device.Index);
        Assert.True(readBack.IsSuccess, readBack.ErrorMessage);
        output.WriteLine($"After write: active '{readBack.Value!.ActiveMode?.Name}', first colors {string.Join(" ", readBack.Value.Colors.Take(4).Select(c => c.ToHex()))}");
        Assert.Equal(perLed.Index, readBack.Value.ActiveModeIndex);
        Assert.Equal(new RgbColor(255, 0, 0), readBack.Value.Colors[0]);

        await Task.Delay(2000);

        var restore = await session.SetModeAsync(device.Index, original);
        Assert.True(restore.IsSuccess, restore.ErrorMessage);
        var restored = await session.GetDeviceAsync(device.Index);
        output.WriteLine($"Restored: active '{restored.Value!.ActiveMode?.Name}'");
        Assert.Equal(original.Index, restored.Value.ActiveModeIndex);

        await host.StopAsync();
    }
    /// <summary>artifacts/diagnostics under the checkout, or the temp folder outside one.</summary>
    private static string DiagnosticsRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ThisIsMyPC.slnx")))
            directory = directory.Parent;
        return directory is null
            ? Path.Combine(Path.GetTempPath(), "tipc-diagnostics")
            : Path.Combine(directory.FullName, "artifacts", "diagnostics");
    }
}
