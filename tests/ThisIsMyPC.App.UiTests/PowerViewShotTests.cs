using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>CI-safe: renders the Power view's System power section with fake scan data.</summary>
public class PowerViewShotTests
{
    private static PowerScanData CreateScanData(bool hibernate = true, bool ultimateInstalled = false)
    {
        var plans = new List<PowerPlan>
        {
            new()
            {
                PlanGuid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
                Name = "Balanced",
                Description = "Automatically balances performance with energy consumption.",
                IsActive = true,
            },
        };
        if (ultimateInstalled)
        {
            plans.Add(new PowerPlan
            {
                PlanGuid = Guid.NewGuid(),
                Name = "Ultimate Performance",
                Description = Modules.Power.Changes.PowerPlanChangeFactory.UltimatePerformanceMarker,
                IsActive = false,
            });
        }

        return new PowerScanData(plans, HibernateEnabled: hibernate, UltimatePerformancePlan: plans.LastOrDefault(
            p => p.Description == Modules.Power.Changes.PowerPlanChangeFactory.UltimatePerformanceMarker));
    }

    // Real registry service: the hibernate toggle reads live state at stage
    // time (read-only; nothing in these tests applies changes).
    private static readonly IRegistryService Registry =
        new ThisIsMyPC.Interop.Win32.Registry.RegistryService();

    [AvaloniaFact]
    public void SystemPowerSection_RendersBothToggles()
    {
        var queue = new PendingChangesService();
        var viewModel = new PowerViewModel(CreateScanData(), queue, registryService: Registry);
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-view", height: 1400);

        session.Screenshot("system-power-section");

        Assert.True(session.IsTextVisible("System power"));
        Assert.True(session.IsTextVisible("Hibernation"));
        Assert.True(session.IsTextVisible("Ultimate Performance plan"));
    }

    [AvaloniaFact]
    public void TogglingHibernateOff_StagesTheChange()
    {
        var queue = new PendingChangesService();
        var viewModel = new PowerViewModel(CreateScanData(hibernate: true), queue, registryService: Registry);
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-view", height: 1400);

        // The VM stages from the toggle state; flipping via binding + command
        // mirrors the ToggleSwitch click without pixel-hunting the switch.
        // Target the opposite of the machine's live state so staging always fires.
        var live = Registry.ReadDWord(
            Modules.Power.Changes.PowerPlanChangeFactory.ModernStandbyKeyPath,
            Modules.Power.Changes.PowerPlanChangeFactory.HibernateValueName);
        var liveEnabled = live is { IsSuccess: true } && live.Value != 0;
        viewModel.IsHibernateEnabled = !liveEnabled;
        viewModel.ToggleHibernateCommand.Execute(null);
        session.Pump();
        session.Screenshot("hibernate-staged");

        Assert.Equal(1, queue.PendingCount);
        Assert.Contains("Hibernation", queue.PendingGroups[0].DisplayName, StringComparison.Ordinal);
    }
}
