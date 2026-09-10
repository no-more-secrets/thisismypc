using ThisIsMyPC.Core;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Interop.Win32.Hardware;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests.Hardware;

/// <summary>
/// Starts the bundled OpenRGB (the pinned archive extracted by
/// tools/get-openrgb-archive.ps1) as a headless server with a throwaway
/// configuration folder, reads every device over the SDK, and stops it.
/// Reads only: no mode, color or brightness is written. Device detection
/// touches USB and, with PawnIO installed, SMBus, which is what the Lighting
/// tab does on open.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class OpenRgbLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BundledServer_StartsAndListsDevices()
    {
        var environment = new Win32HardwareProbeEnvironment();
        var bundled = environment.BundledCompanionExecutable(CompanionApp.OpenRgb);
        output.WriteLine($"Bundled OpenRGB: {bundled ?? "not found (run tools/get-openrgb-archive.ps1)"}");
        Assert.NotNull(bundled);

        var config = Path.Combine(DiagnosticsRoot(), "openrgb-live", Guid.NewGuid().ToString("N"));
        using var host = new BundledOpenRgbHost(environment, config);
        var started = DateTimeOffset.Now;
        var start = await host.EnsureRunningAsync();
        output.WriteLine($"Start: {(start.IsSuccess ? "ok" : start.ErrorMessage)} after {(DateTimeOffset.Now - started).TotalSeconds:0.0} s, state {host.State}");
        Assert.True(start.IsSuccess, start.ErrorMessage);

        var connect = await new OpenRgbSdkClient().ConnectAsync(host.Port);
        Assert.True(connect.IsSuccess, connect.ErrorMessage);
        using var session = connect.Value!;
        output.WriteLine($"Protocol version in use: {session.ProtocolVersion}");

        var devices = await session.GetDevicesAsync();
        Assert.True(devices.IsSuccess, devices.ErrorMessage);
        foreach (var device in devices.Value!)
        {
            output.WriteLine($"[{device.Index}] {device.Name} ({LightingDevice.DescribeType(device.Type)}, {device.Vendor}) active mode {device.ActiveModeIndex}, {device.Zones.Count} zones, {device.Leds.Count} LEDs, {device.Colors.Count} colors, location {device.Location}");
            foreach (var mode in device.Modes)
            {
                output.WriteLine($"    mode {mode.Index} {mode.Name}: flags {mode.Flags}, speed {mode.SpeedMin}-{mode.SpeedMax} ({mode.Speed}), brightness {mode.BrightnessMin}-{mode.BrightnessMax} ({mode.Brightness}), colors {mode.ColorsMin}-{mode.ColorsMax} [{string.Join(",", mode.Colors.Select(c => c.ToHex()))}], color mode {mode.ColorMode}, direction {mode.Direction}");
            }
            foreach (var zone in device.Zones)
                output.WriteLine($"    zone {zone.Index} {zone.Name}: {zone.Type}, {zone.LedCount} LEDs ({zone.LedsMin}-{zone.LedsMax}), matrix {zone.MatrixHeight}x{zone.MatrixWidth}, {zone.Segments.Count} segments");
            output.WriteLine($"    first colors: {string.Join(" ", device.Colors.Take(8).Select(c => c.ToHex()))}");
        }

        await host.StopAsync();
        output.WriteLine($"Stopped; state {host.State}");
        Assert.Equal(OpenRgbHostState.Stopped, host.State);
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
