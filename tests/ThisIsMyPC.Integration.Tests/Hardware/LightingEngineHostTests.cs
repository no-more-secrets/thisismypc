using ThisIsMyPC.Interop.Win32.Hardware;

namespace ThisIsMyPC.Integration.Tests.Hardware;

/// <summary>
/// Runs the built lighting engine (artifacts/lighting-engine/Release) as the app
/// would: hidden child process, loopback port, SDK handshake, rescan, stop. It
/// detects real devices, so it is Diagnostic. Build the engine first with
/// MSBuild on src/ThisIsMyPC.LightingEngine/ThisIsMyPC.LightingEngine.vcxproj.
/// </summary>
public class LightingEngineHostTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Diagnostic")]
    public async Task Engine_StartsServesRescansAndStops()
    {
        var enginePath = Path.Combine(FindRepoRoot(), "artifacts", "lighting-engine", "Release", "ThisIsMyPC-LightingEngine.exe");
        Assert.True(File.Exists(enginePath), $"Build the engine first: {enginePath}");
        var configDirectory = Path.Combine(Path.GetTempPath(), "ThisIsMyPC-engine-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = new LightingEngineHost(configDirectory, enginePath);
            Assert.True(host.IsAvailable);

            var started = await host.StartAsync();
            Assert.True(started.IsSuccess, started.ErrorMessage + " | " + string.Join(" | ", host.Notes));
            Assert.InRange(started.Value, 1024, 65535);
            var again = await host.StartAsync();
            Assert.Equal(started.Value, again.Value);

            var connected = await new OpenRgbSdkClient().ConnectAsync(started.Value);
            Assert.True(connected.IsSuccess, connected.ErrorMessage);
            using (var session = connected.Value!)
            {
                var devices = await session.GetDevicesAsync();
                Assert.True(devices.IsSuccess, devices.ErrorMessage);
                output.WriteLine($"Engine on port {started.Value} lists {devices.Value!.Count} device(s):");
                foreach (var device in devices.Value)
                {
                    Assert.False(string.IsNullOrWhiteSpace(device.Name));
                    output.WriteLine($"  {device.Name} [{device.Type}] {device.Description} @ {device.Location}; modes {device.Modes.Count}, active {device.ActiveMode?.Name}, leds {device.Leds.Count}");
                }
            }
            foreach (var note in host.Notes)
                output.WriteLine("engine: " + note);

            var rescanned = await host.RescanAsync();
            Assert.True(rescanned.IsSuccess, rescanned.ErrorMessage);

            host.Stop();
            var afterStop = await new OpenRgbSdkClient().ConnectAsync(started.Value);
            Assert.False(afterStop.IsSuccess);
            Assert.True(File.Exists(Path.Combine(configDirectory, "OpenRGB.json")), "The engine keeps its settings under the folder it was given.");
        }
        finally
        {
            try { Directory.Delete(configDirectory, recursive: true); } catch (IOException) { }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ThisIsMyPC.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
