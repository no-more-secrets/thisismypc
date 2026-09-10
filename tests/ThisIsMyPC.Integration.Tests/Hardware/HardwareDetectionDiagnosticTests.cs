using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Interop.Com.Tasks;
using ThisIsMyPC.Interop.Win32.Hardware;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Interop.Win32.Services;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests.Hardware;

/// <summary>
/// Runs the module-level detection pass on this machine over the real shared
/// inventory and prints every fact, the launch paths, the notes and the
/// policy's decision per tab. Read-only: registry reads, the firmware table,
/// present devices, a process list, one localhost TCP probe when OpenRGB
/// runs. Nothing is installed, launched or written.
/// </summary>
[Trait("Category", "Diagnostic")]
public sealed class HardwareDetectionDiagnosticTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DetectAndDecide_OnThisMachine()
    {
        var registry = new RegistryService();
        using var provider = new HardwareFactsProvider(
            new HardwareDetectionService(registry),
            registry,
            new Win32HardwareProbeEnvironment(),
            new ScheduledTaskService(),
            new ServiceControlService());

        var started = DateTimeOffset.Now;
        var snapshot = await provider.GetAsync();
        var elapsed = DateTimeOffset.Now - started;
        var facts = snapshot.Facts;

        output.WriteLine($"Detection took {elapsed.TotalMilliseconds:0} ms (inventory observed {snapshot.Inventory.ObservedAt:HH:mm:ss}, {snapshot.Inventory.Devices.Count} devices)");
        output.WriteLine($"Identity: {facts.Identity.Manufacturer} / {facts.Identity.Model} / vendor {facts.Identity.Vendor}");
        output.WriteLine($"Board: {snapshot.Inventory.Firmware.BoardManufacturer} {snapshot.Inventory.Firmware.BoardProduct}; chipset {snapshot.Inventory.Chipset.Name} ({snapshot.Inventory.Chipset.Source})");
        output.WriteLine($"Chassis types: {(facts.FormFactor.SmbiosChassisTypes is { } c ? string.Join(",", c) : "null")}");
        output.WriteLine($"Platform role: {facts.FormFactor.PlatformRole?.ToString() ?? "null"}");
        output.WriteLine($"Battery: {facts.FormFactor.HasSystemBattery?.ToString() ?? "null"}; panel: {facts.FormFactor.HasInternalDisplayPanel?.ToString() ?? "null"}");
        output.WriteLine($"ATKACPI: {facts.AsusPlatformDriverPresent?.ToString() ?? "null"}");
        output.WriteLine($"OpenRGB server: reachable={facts.OpenRgbServerReachable?.ToString() ?? "null"} count={facts.OpenRgbDeviceCount?.ToString() ?? "null"}");
        foreach (var companion in facts.Companions)
        {
            output.WriteLine($"  {companion.App}: installed={companion.IsInstalled} running={companion.IsRunning} owns=[{string.Join(",", companion.ObservedOwnership)}] launch={snapshot.LaunchPathOf(companion.App) ?? "-"}");
        }
        output.WriteLine("Notes:");
        foreach (var note in snapshot.Notes)
            output.WriteLine("  " + note);

        var report = HardwareCompatibilityPolicy.Decide(facts);
        output.WriteLine($"Form factor decision: {report.FormFactor.FormFactor} ({string.Join(" | ", report.FormFactor.Reasons)})");
        foreach (var tab in report.Tabs)
        {
            output.WriteLine($"{tab.Domain}: {tab.Availability}, ops={tab.Operations}, action={tab.Action?.Kind.ToString() ?? "-"} {tab.Action?.App.ToString() ?? ""}");
            output.WriteLine($"  {tab.Explanation}");
            foreach (var line in tab.Evidence)
                output.WriteLine($"  evidence: {line}");
            foreach (var line in tab.ConflictNotes)
                output.WriteLine($"  conflict: {line}");
        }

        Assert.Equal(Enum.GetValues<CompanionApp>().Length, facts.Companions.Count);
        Assert.NotNull(facts.FormFactor.SmbiosChassisTypes);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"detection took {elapsed.TotalSeconds:0.0} s");
    }
}
