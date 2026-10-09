using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.UiTests;

public sealed class PowerPolicyShotTests
{
    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task LivePowerPageReadOnly()
    {
        using var session = UiSession.ForMainWindow("power-policy-live");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, what: "sidebar load");
        session.OpenModule("Power Plans");
        await session.WaitForAsync(() => main.CurrentContent is PowerViewModel, what: "power page load");
        session.Screenshot("power");
    }

    [AvaloniaTheory]
    [InlineData("Professional")]
    [InlineData("Core")]
    public async Task SavedPoliciesRemainEditableWithoutErrorText(string edition)
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        registry.WriteDWord(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath, "ACSettingIndex", 0);
        registry.WriteDWord(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath, "DCSettingIndex", 0);
        var plan = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        registry.WriteString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, "ActivePowerScheme", plan.ToString("D"));
        var reader = new PolicyControlStateReader(registry, () => [new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
        [
            new(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath[5..], "ACSettingIndex", 4, [0, 0, 0, 0]),
            new(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath[5..], "DCSettingIndex", 4, [0, 0, 0, 0]),
            new(PowerPlanChangeFactory.ActivePlanPolicyKeyPath[5..], "ActivePowerScheme", 1, [.. Encoding.Unicode.GetBytes(plan.ToString("D") + "\0")]),
        ])]);
        var queue = new PendingChangesService(policyStates: reader, capabilityDetector: new CapabilityDetector(registry));
        using var vm = new PowerViewModel(new([new() { PlanGuid = plan, Name = "Balanced", IsActive = true }],
            HibernateEnabled: false, PolicyPinnedPlan: plan, SleepPolicy: new(0, 0)), queue, registryService: registry, policyStates: reader);
        using var session = UiSession.ForView(new PowerView(), vm, "power-policy", width: 1100, height: 1000);
        var sleep = vm.SystemPowerToggles.Single(r => r.Label == "Allow sleep");
        var pin = vm.SystemPowerToggles.Single(r => r.Label == "Pin the active plan by policy");
        Assert.Null(sleep.WarningText);
        Assert.Null(pin.WarningText);
        Assert.Equal(edition != "Core", sleep.IsToggleEnabled);
        Assert.Equal(edition != "Core", pin.IsToggleEnabled);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot(edition + "-" + theme.Key);
        }
        if (edition == "Core") return;
        session.Click(session.Find<ToggleSwitch>(c => ReferenceEquals(c.DataContext, sleep)));
        await session.WaitForAsync(() => queue.PendingCount == 1, what: "sleep policy staging");
        session.Click(session.Find<ToggleSwitch>(c => ReferenceEquals(c.DataContext, pin)));
        await session.WaitForAsync(() => queue.PendingCount == 2, what: "pin removal staging");
        Assert.All(queue.PendingGroups.SelectMany(g => g.Changes), c => Assert.Equal(new LocalPolicyValue(null, ""), LocalPolicyValue.Decode(c.AfterValue)));
        session.Screenshot("staged");
        session.Click(session.Find<ToggleSwitch>(c => ReferenceEquals(c.DataContext, sleep)));
        session.Click(session.Find<ToggleSwitch>(c => ReferenceEquals(c.DataContext, pin)));
        await session.WaitForAsync(() => queue.PendingCount == 0, what: "discard both policies");
        Assert.Equal(0, registry.ReadDWord(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath, "ACSettingIndex").Value);
        Assert.Equal(plan.ToString("D"), registry.ReadString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, "ActivePowerScheme").Value);
    }
}
