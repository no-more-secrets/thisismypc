using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests;

public sealed class HibernateAndUltimatePerformanceTests
{
    private static readonly Guid BalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    private readonly FakePowerService _power = new();
    private readonly FakeRegistryService _registry = new();

    private PowerModule Module => new(_power, _registry);

    // ---- Hibernation ----

    [Fact]
    public async Task Scan_ReadsHibernateStateFromRegistry()
    {
        _registry.SetDWord(PowerPlanChangeFactory.ModernStandbyKeyPath,
            PowerPlanChangeFactory.HibernateValueName, 1);

        var result = await Module.ScanSystemStateAsync();

        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.True(data.HibernateEnabled);
    }

    [Fact]
    public async Task Scan_UnreadableHibernateStateIsNull()
    {
        var result = await Module.ScanSystemStateAsync();

        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.Null(data.HibernateEnabled);
    }

    [Fact]
    public async Task Apply_HibernateToggleRoutesToPowerService()
    {
        var change = PowerPlanChangeFactory.CreateHibernateToggle(currentlyEnabled: true, enable: false);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.False(_power.HibernateEnabled);
        Assert.Contains("SetHibernateEnabled:False", _power.Calls);
    }

    [Fact]
    public async Task Revert_HibernateToggleReenables()
    {
        _power.HibernateEnabled = false;
        var change = PowerPlanChangeFactory.CreateHibernateToggle(currentlyEnabled: true, enable: false);
        var swapped = change with
        {
            BeforeValue = change.AfterValue!,
            AfterValue = change.BeforeValue,
        };

        var result = await Module.RevertChangeAsync(swapped);

        Assert.True(result.IsSuccess);
        Assert.True(_power.HibernateEnabled);
    }

    // ---- Ultimate Performance ----

    [Fact]
    public async Task Scan_DetectsUltimatePerformanceByMarker()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(Guid.NewGuid(), "Höchstleistung",
            description: PowerPlanChangeFactory.UltimatePerformanceMarker);

        var result = await Module.ScanSystemStateAsync();

        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.NotNull(data.UltimatePerformancePlan);
    }

    [Fact]
    public async Task Scan_DetectsForeignUltimatePerformanceByName()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(Guid.NewGuid(), "Ultimate Performance");

        var result = await Module.ScanSystemStateAsync();

        var data = Assert.IsType<PowerScanData>(result.Value);
        Assert.NotNull(data.UltimatePerformancePlan);
    }

    [Fact]
    public async Task Apply_InstallDuplicatesAndMarksThePlan()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        var change = PowerPlanChangeFactory.CreateUltimatePerformanceToggle(
            currentlyInstalled: false, install: true);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Contains(_power.Calls, c => c.StartsWith(
            $"DuplicateScheme:{PowerPlanChangeFactory.UltimatePerformanceSourceGuid:D}",
            StringComparison.Ordinal));
        Assert.Contains(_power.Calls, c => c.StartsWith("WriteSchemeText:", StringComparison.Ordinal)
            && c.EndsWith(":Ultimate Performance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_RemoveDeletesTheMarkedPlan()
    {
        var upGuid = Guid.NewGuid();
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        _power.AddPlan(upGuid, "Ultimate Performance",
            description: PowerPlanChangeFactory.UltimatePerformanceMarker);
        var change = PowerPlanChangeFactory.CreateUltimatePerformanceToggle(
            currentlyInstalled: true, install: false);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        Assert.Contains($"DeleteScheme:{upGuid:D}", _power.Calls);
        Assert.Null(_power.GetPlan(upGuid));
    }

    [Fact]
    public async Task Apply_RemoveRefusesWhileActive()
    {
        var upGuid = Guid.NewGuid();
        _power.AddPlan(upGuid, "Ultimate Performance", isActive: true,
            description: PowerPlanChangeFactory.UltimatePerformanceMarker);
        var change = PowerPlanChangeFactory.CreateUltimatePerformanceToggle(
            currentlyInstalled: true, install: false);

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(_power.Calls, c => c.StartsWith("DeleteScheme:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_RemoveWhenAlreadyGoneSucceeds()
    {
        _power.AddPlan(BalancedGuid, "Balanced", isActive: true);
        var change = PowerPlanChangeFactory.CreateUltimatePerformanceToggle(
            currentlyInstalled: true, install: false);

        var result = await Module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Apply_InstallFailureSurfaces()
    {
        _power.InjectFailure("DuplicateScheme", ErrorCategory.AccessDenied);
        var change = PowerPlanChangeFactory.CreateUltimatePerformanceToggle(
            currentlyInstalled: false, install: true);

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }
}
