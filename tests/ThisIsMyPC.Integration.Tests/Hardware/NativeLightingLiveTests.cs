using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Interop.Win32.Hardware.Hid;
using ThisIsMyPC.Interop.Win32.Hardware.I2c;
using ThisIsMyPC.Interop.Win32.Security;
using ThisIsMyPC.Lighting;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests.Hardware;

/// <summary>
/// The built-in lighting controllers against this PC: HID enumeration over
/// hid.dll, GPU I2C over NvAPI, every registered detector. Reads only unless
/// TIPC_LIGHTING_WRITE=1, which paints each per-LED device one color for two
/// seconds and restores its mode. Nothing is saved to a device.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class NativeLightingLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Detect_ListsThisPcsDevices()
    {
        using var backend = new NativeLightingBackend(new WindowsHidTransport(), new NvApiI2cBusProvider());

        var started = DateTimeOffset.Now;
        var detected = await backend.DetectAsync();
        output.WriteLine($"Detection took {(DateTimeOffset.Now - started).TotalMilliseconds:0} ms");
        Assert.True(detected.IsSuccess, detected.ErrorMessage);
        foreach (var note in detected.Value!.Notes)
            output.WriteLine("  note: " + note);
        foreach (var summary in detected.Value.Devices)
            output.WriteLine($"  device: {summary.Name} ({summary.Type}) via {summary.Controller} at {summary.Location}");

        var opened = await backend.OpenAsync();
        Assert.True(opened.IsSuccess, opened.ErrorMessage);
        using var session = opened.Value!;
        var devices = await session.GetDevicesAsync();
        Assert.True(devices.IsSuccess, devices.ErrorMessage);
        foreach (var device in devices.Value!)
        {
            output.WriteLine($"[{device.Index}] {device.Name} ({LightingDevice.DescribeType(device.Type)}, {device.Vendor}, v{device.Version}, serial {device.Serial}) active mode {device.ActiveModeIndex}, {device.Zones.Count} zones, {device.Leds.Count} LEDs, location {device.Location}");
            foreach (var mode in device.Modes)
                output.WriteLine($"    mode {mode.Index} {mode.Name}: flags {mode.Flags}, speed {mode.SpeedMin}-{mode.SpeedMax} ({mode.Speed}), brightness {mode.BrightnessMin}-{mode.BrightnessMax} ({mode.Brightness}), colors [{string.Join(",", mode.Colors.Select(c => c.ToHex()))}], color mode {mode.ColorMode}, direction {mode.Direction}");
            foreach (var zone in device.Zones)
                output.WriteLine($"    zone {zone.Index} {zone.Name}: {zone.Type}, {zone.LedCount} LEDs");
            output.WriteLine($"    first colors: {string.Join(" ", device.Colors.Take(8).Select(c => c.ToHex()))}");
        }
        Assert.Equal(detected.Value.Devices.Count, devices.Value.Count);
    }

    [Fact]
    public async Task PerLedColor_WritesAndRestores()
    {
        if (Environment.GetEnvironmentVariable("TIPC_LIGHTING_WRITE") != "1")
        {
            output.WriteLine("Skipped: set TIPC_LIGHTING_WRITE=1 to write to real lighting.");
            return;
        }

        using var backend = new NativeLightingBackend(new WindowsHidTransport(), new NvApiI2cBusProvider());
        using var session = (await backend.OpenAsync()).Value!;
        var devices = (await session.GetDevicesAsync()).Value!;
        Assert.NotEmpty(devices);

        foreach (var device in devices)
        {
            var perLed = device.Modes.FirstOrDefault(m => m.HasPerLedColor);
            if (perLed is null || device.Leds.Count == 0)
                continue;
            var original = device.ActiveMode ?? device.Modes[0];
            var originalColors = device.Colors.ToList();
            output.WriteLine($"{device.Name}: switching from {original.Name} to {perLed.Name}, painting {device.Leds.Count} LEDs red");

            var set = await session.SetModeAsync(device.Index, perLed);
            Assert.True(set.IsSuccess, set.ErrorMessage);
            var painted = await session.SetLedsAsync(device.Index, Enumerable.Repeat(new RgbColor(255, 0, 0), device.Leds.Count).ToList());
            Assert.True(painted.IsSuccess, painted.ErrorMessage);
            await Task.Delay(2000);

            var restoredColors = await session.SetLedsAsync(device.Index, originalColors);
            Assert.True(restoredColors.IsSuccess, restoredColors.ErrorMessage);
            var restored = await session.SetModeAsync(device.Index, original);
            Assert.True(restored.IsSuccess, restored.ErrorMessage);
            var after = await session.GetDeviceAsync(device.Index);
            output.WriteLine($"{device.Name}: restored to {after.Value?.ActiveMode?.Name}");
        }
    }
}

/// <summary>
/// The release question: nvapi64.dll mapped before Code Integrity Guard keeps
/// working after it. Turns CIG on in this test process, so it must run alone
/// (dotnet test --filter FullyQualifiedName~NvApiUnderCig); every later
/// non-Microsoft image load in the process fails by design.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class NvApiUnderCigTests(ITestOutputHelper output)
{
    [Fact]
    public async Task NvApi_MappedBeforeCig_StillDrivesGpuI2cAfterCig()
    {
        var path = Path.Combine(Environment.SystemDirectory, "nvapi64.dll");
        if (!File.Exists(path))
        {
            output.WriteLine("Skipped: no nvapi64.dll (no NVIDIA driver).");
            return;
        }
        var trust = AuthenticodeVerifier.VerifyTrusted(path, "NVIDIA Corporation");
        output.WriteLine($"nvapi64.dll signature: {(trust.IsSuccess ? "NVIDIA Corporation" : trust.ErrorMessage)}");
        Assert.True(trust.IsSuccess, trust.ErrorMessage);

        var handle = System.Runtime.InteropServices.NativeLibrary.Load(path);
        Assert.NotEqual(0, handle);

        // The shipped app is one native image; this test host loads managed
        // assemblies as images, which CIG then refuses (NLog is unsigned).
        // Touch everything the probe needs before the policy closes.
        using var provider = new NvApiI2cBusProvider();
        using var backend = new NativeLightingBackend(null, provider);
        NLog.LogManager.GetLogger("warmup").Debug("warm");
        _ = typeof(ThisIsMyPC.Lighting.Controllers.Ene.EneSmBusController).Assembly;
        _ = ThisIsMyPC.Lighting.Detection.LightingDetectors.I2cPci.Count;

        Assert.True(BinarySignatureHardening.Apply(), "CIG could not be enabled");
        Assert.True(BinarySignatureHardening.IsEnabled());
        output.WriteLine("CIG on; probing the GPU bus through the pre-mapped NvAPI");

        var notes = new List<string>();
        var buses = provider.Enumerate(notes);
        foreach (var note in notes)
            output.WriteLine("  " + note);
        Assert.NotEmpty(buses);

        var detected = await backend.DetectAsync();
        Assert.True(detected.IsSuccess, detected.ErrorMessage);
        foreach (var device in detected.Value!.Devices)
            output.WriteLine($"  device: {device.Name} via {device.Controller} at {device.Location}");
        foreach (var note in detected.Value.Notes)
            output.WriteLine("  note: " + note);
    }
}
