using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Power.Services;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests.Services;

public sealed class PowerPlanScannerTests
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private readonly FakePowerService _power = new();

    [Fact]
    public void Scan_MapsAllPlanFields()
    {
        _power.AddPlan(Balanced, "Balanced", "Automatically balances performance", isActive: true);
        _power.AddPlan(HighPerformance, "High performance", description: null);

        var scanner = new PowerPlanScanner(_power);
        var plans = scanner.Scan();

        Assert.Null(scanner.LastScanError);
        Assert.Equal(2, plans.Count);

        var balanced = plans.Single(p => p.PlanGuid == Balanced);
        Assert.Equal("Balanced", balanced.Name);
        Assert.Equal("Automatically balances performance", balanced.Description);
        Assert.True(balanced.IsActive);
        Assert.False(balanced.IsNormallyHidden);

        var high = plans.Single(p => p.PlanGuid == HighPerformance);
        Assert.Null(high.Description);
        Assert.False(high.IsActive);
    }

    [Fact]
    public void Scan_OrdersActiveThenStockThenUltimateThenCustomByName()
    {
        // Registered in GUID order, the way Windows enumerates them
        _power.AddPlan(Balanced, "Balanced");
        _power.AddPlan(HighPerformance, "High performance");
        _power.AddPlan(new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"), "Power saver");
        _power.AddPlan(new Guid("ceedc97a-f768-4c61-9167-99838f5ebace"), "ChrisTitus - Ultimate Power Plan", isActive: true);
        _power.AddPlan(new Guid("d0000000-0000-0000-0000-000000000001"), "Ultimate Performance", ThisIsMyPC.Modules.Power.Changes.PowerPlanChangeFactory.UltimatePerformanceMarker);
        _power.AddPlan(new Guid("00000000-0000-0000-0000-000000000009"), "Zeta");
        _power.AddPlan(new Guid("f0000000-0000-0000-0000-000000000002"), "alpha");

        var plans = new PowerPlanScanner(_power).Scan();

        Assert.Equal(
            ["ChrisTitus - Ultimate Power Plan", "Balanced", "Power saver", "High performance", "Ultimate Performance", "alpha", "Zeta"],
            plans.Select(p => p.Name));
    }

    [Fact]
    public void Scan_FlagsNormallyHiddenPlans()
    {
        _power.AddPlan(UltimatePerformance, "Ultimate Performance");

        var plans = new PowerPlanScanner(_power).Scan();

        Assert.True(plans.Single().IsNormallyHidden);
    }

    [Fact]
    public void Scan_EnumerationFailure_ReportsErrorAndEmptyList()
    {
        _power.InjectFailure("EnumeratePlans", ErrorCategory.ServiceUnavailable);

        var scanner = new PowerPlanScanner(_power);
        var plans = scanner.Scan();

        Assert.Empty(plans);
        Assert.Equal("Injected EnumeratePlans failure.", scanner.LastScanError);
    }

    [Fact]
    public void Scan_ClearsPreviousError()
    {
        _power.InjectFailure("EnumeratePlans");
        var scanner = new PowerPlanScanner(_power);
        scanner.Scan();
        Assert.NotNull(scanner.LastScanError);

        var recovered = new FakePowerService();
        recovered.AddPlan(Balanced, "Balanced", isActive: true);
        var freshScanner = new PowerPlanScanner(recovered);
        freshScanner.Scan();
        Assert.Null(freshScanner.LastScanError);
    }
}
