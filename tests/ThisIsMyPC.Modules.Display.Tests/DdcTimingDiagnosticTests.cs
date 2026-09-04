using System.Diagnostics;
using ThisIsMyPC.Interop.Win32.Display;

namespace ThisIsMyPC.Modules.Display.Tests;

/// <summary>
/// Times the live DDC/CI scan on this machine: a cold enumeration (capabilities
/// requests included) and a warm one (capabilities cached), with per-monitor
/// detail. Writes artifacts/diagnostics/display-timing/timing.txt. Diagnostic:
/// talks to the real monitors.
/// </summary>
[Trait("Category", "Diagnostic")]
public class DdcTimingDiagnosticTests
{
    [Fact]
    public void TimeColdAndWarmScans()
    {
        var service = new DdcMonitorService();
        var lines = new List<string>();

        for (var pass = 0; pass < 3; pass++)
        {
            var sw = Stopwatch.StartNew();
            var result = service.EnumerateMonitors();
            sw.Stop();
            lines.Add($"pass {pass} ({(pass == 0 ? "cold" : "warm")}): {sw.ElapsedMilliseconds} ms, success={result.IsSuccess} {result.ErrorMessage}");
            foreach (var m in result.Value ?? [])
            {
                lines.Add($"  {m.Name} [{m.Id}] ddc={m.SupportsDdc} bright={m.Brightness}/{m.BrightnessMax} contrast={m.Contrast} input={m.CurrentInput} inputs={m.InputSources.Count} vendor={m.VendorFeatures.Count} err={m.DdcError}");
                foreach (var f in m.VendorFeatures)
                    lines.Add($"    0x{f.Code:X2} {f.Name} current={f.Current} values={f.Values.Count}");
            }
        }

        var dir = Path.Combine(FindRepoRoot(), "artifacts", "diagnostics", "display-timing");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "timing.txt"), lines);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThisIsMyPC.sln")) && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
