using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// Power view's System power section. Uses the real RegistryService for live
/// state reads, so this is Category=Diagnostic per CLAUDE.md (never in CI);
/// it stages only, never applies.
/// </summary>
[Trait("Category", "Diagnostic")]
public class PowerViewShotTests
{
    private static PowerScanData CreateScanData(bool hibernate = true, Guid? pinnedPlan = null)
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
        return new PowerScanData(plans, HibernateEnabled: hibernate, PolicyPinnedPlan: pinnedPlan, ActivePlanLockedByPolicy: pinnedPlan is not null);
    }

    [AvaloniaFact]
    public void SystemPowerSection_ShowsThePolicyPinRowWhenAPinExists()
    {
        var queue = new PendingChangesService();
        var pinned = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        var viewModel = new PowerViewModel(CreateScanData(pinnedPlan: pinned), queue, registryService: Registry);
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-view", height: 1400);

        session.Screenshot("policy-pin-row");

        Assert.True(session.IsTextVisible("Pin the active plan by policy"));
        Assert.Contains(viewModel.SystemPowerToggles, r => r.Label == "Pin the active plan by policy" && r.IsEnabled);
    }

    private static readonly IRegistryService Registry =
        new ThisIsMyPC.Interop.Win32.Registry.RegistryService();

    [AvaloniaFact]
    public void SystemPowerSection_RendersHibernateRow()
    {
        var queue = new PendingChangesService();
        var viewModel = new PowerViewModel(CreateScanData(), queue, registryService: Registry);
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-view", height: 1400);

        session.Screenshot("system-power-section");

        Assert.True(session.IsTextVisible("System power"));
        Assert.True(session.IsTextVisible("Allow hibernation"));
        // No power service in this session — the Ultimate Performance row must not render.
        Assert.False(session.IsTextVisible("Add the Ultimate Performance plan"));
    }

    [AvaloniaFact]
    public async Task TogglingHibernate_StagesTheChange()
    {
        // Seed the scan with the machine's live state so flipping the row is
        // guaranteed to differ from the live read at stage time.
        var live = Registry.ReadDWord(
            Modules.Power.Changes.PowerPlanChangeFactory.ModernStandbyKeyPath,
            Modules.Power.Changes.PowerPlanChangeFactory.HibernateValueName);
        var liveEnabled = live is { IsSuccess: true } && live.Value != 0;

        var queue = new PendingChangesService();
        var viewModel = new PowerViewModel(CreateScanData(liveEnabled), queue, registryService: Registry);
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-view", height: 1400);

        var row = viewModel.SystemPowerToggles.First(r => r.Label == "Allow hibernation");
        row.IsEnabled = !liveEnabled;
        await session.WaitForAsync(() => queue.PendingCount == 1, timeoutMs: 5000, what: "hibernate staging");
        session.Screenshot("hibernate-staged");

        row.IsEnabled = !row.IsEnabled;
        await session.WaitForAsync(() => queue.PendingCount == 0, timeoutMs: 5000, what: "hibernate unstaging");
    }
}
