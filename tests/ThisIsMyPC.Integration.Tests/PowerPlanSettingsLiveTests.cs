using System.Diagnostics;
using ThisIsMyPC.Interop.Win32.Power;
using Xunit;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests;

/// <summary>
/// Reads the active plan's settings through the real powrprof calls. Guards
/// the 2026-09-02 hang: PowerIsSettingRangeDefined said false for range
/// settings and the possible-values walk never ended.
/// </summary>
[Trait("Category", "Diagnostic")]
public class PowerPlanSettingsLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ActivePlanSettings_LoadWithinSeconds_AndRangesHaveBounds()
    {
        var service = new PowerService();
        var plans = service.EnumeratePlans();
        Assert.True(plans.IsSuccess, plans.ErrorMessage);
        var active = Assert.Single(plans.Value!, p => p.IsActive);

        var clock = Stopwatch.StartNew();
        var load = Task.Run(() => service.EnumeratePlanSettings(active.PlanGuid));
        var finished = await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.True(finished == load, "Enumerating the active plan's settings did not finish in 30 seconds.");

        var result = await load;
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var settings = result.Value!;
        output.WriteLine($"{active.Name}: {settings.Count} settings in {clock.ElapsedMilliseconds} ms");
        Assert.NotEmpty(settings);

        foreach (var setting in settings)
        {
            if (setting.IsRange)
                Assert.True(setting.Max >= setting.Min, $"{setting.Name}: Max {setting.Max} below Min {setting.Min}");
            else
                Assert.True(setting.PossibleValues.Count < 64, $"{setting.Name}: {setting.PossibleValues.Count} possible values reads as a runaway walk");
        }

        var hardDiskTimeout = settings.FirstOrDefault(s => s.SettingGuid == new Guid("6738e2c4-e8a5-4a42-b16a-e040e769756e"));
        if (hardDiskTimeout is not null)
            Assert.True(hardDiskTimeout.IsRange, "Turn off hard disk after must read as a range.");
    }
}
