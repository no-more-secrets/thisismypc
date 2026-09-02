using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// A plan's settings panel: one tab per subgroup, the strip wrapping, rows
/// under the selected tab only. CI-safe: a fake power service hands back a
/// few groups of settings.
/// </summary>
public class PowerSettingsShotTests
{
    private static PowerScanData ScanData() => new(
    [
        new PowerPlan
        {
            PlanGuid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
            Name = "Balanced",
            Description = "Automatically balances performance with energy consumption on capable hardware.",
            IsActive = true,
        },
    ], HibernateEnabled: true);

    [AvaloniaFact]
    public async Task Settings_OpenAsOneTabPerGroup()
    {
        var changes = new PendingChangesService();
        var viewModel = new PowerViewModel(ScanData(), changes, powerService: new SettingsFakePowerService());
        using var session = UiSession.ForView(new PowerView(), viewModel, "power-settings", height: 800);

        session.ClickText("Settings");
        await session.WaitForAsync(() => !viewModel.IsLoadingSettings, what: "settings load");
        session.Pump();
        session.Screenshot("settings-tabs");

        Assert.True(viewModel.IsSettingsView);
        Assert.Equal(["Hard disk (2)", "Sleep (3)", "Processor power management (2)", "Display (1)"],
            viewModel.SettingsGroups.Select(g => g.Header));
        var tabs = session.Find<TabControl>(t => t.ItemsSource == viewModel.SettingsGroups);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(session.IsTextVisible("Turn off hard disk after"));
        Assert.False(session.IsTextVisible("Sleep after"));

        session.ClickText("Sleep (3)");
        session.Screenshot("settings-sleep-tab");
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.True(session.IsTextVisible("Sleep after"));
        Assert.True(session.IsTextVisible("Allow wake timers"));
        Assert.False(session.IsTextVisible("Turn off hard disk after"));

        session.ClickText("← Back to plans");
        Assert.False(viewModel.IsSettingsView);
    }
}

file sealed class SettingsFakePowerService : IPowerService
{
    private static readonly Guid HardDisk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    private static readonly Guid Sleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid Display = new("7516b95f-f776-4464-8c53-06167f40cc99");

    private static PowerSettingInfo Range(Guid group, string groupName, string name, string units, uint ac, uint dc, uint max) =>
        new(group, groupName, Guid.NewGuid(), name, null, ac, dc, units, IsRange: true, 0, max, 1, []);

    private static PowerSettingInfo Choice(Guid group, string groupName, string name, uint ac, uint dc, params string[] options) =>
        new(group, groupName, Guid.NewGuid(), name, null, ac, dc, null, IsRange: false, 0, 0, 1,
            options.Select((o, i) => new PowerPossibleValue((uint)i, o)).ToList());

    public OperationResult<IReadOnlyList<PowerPlanInfo>> EnumeratePlans() =>
        OperationResult<IReadOnlyList<PowerPlanInfo>>.Success([]);
    public OperationResult<bool> SetActivePlan(Guid planGuid) => OperationResult<bool>.Success(true);
    public OperationResult<IReadOnlyList<PowerSettingInfo>> EnumeratePlanSettings(Guid planGuid) =>
        OperationResult<IReadOnlyList<PowerSettingInfo>>.Success(
        [
            Range(HardDisk, "Hard disk", "Turn off hard disk after", "Seconds", 1200, 600, 4294967295),
            Choice(HardDisk, "Hard disk", "AHCI Link Power Management - HIPM/DIPM", 0, 1, "Active", "HIPM", "HIPM+DIPM", "DIPM", "Lowest"),
            Range(Sleep, "Sleep", "Sleep after", "Seconds", 1800, 900, 4294967295),
            Choice(Sleep, "Sleep", "Allow wake timers", 1, 0, "Disable", "Enable", "Important Wake Timers Only"),
            Range(Sleep, "Sleep", "Hibernate after", "Seconds", 0, 3600, 4294967295),
            Range(Processor, "Processor power management", "Minimum processor state", "%", 5, 5, 100),
            Range(Processor, "Processor power management", "Maximum processor state", "%", 100, 100, 100),
            Range(Display, "Display", "Turn off display after", "Seconds", 600, 300, 4294967295),
        ]);
    public OperationResult<bool> WriteSettingIndex(Guid planGuid, Guid subgroupGuid, Guid settingGuid, bool ac, uint valueIndex) =>
        OperationResult<bool>.Success(true);
    public bool SupportsModernStandby() => false;
    public OperationResult<bool> SetHibernateEnabled(bool enable) => OperationResult<bool>.Success(true);
    public OperationResult<Guid> DuplicateScheme(Guid sourceSchemeGuid) => OperationResult<Guid>.Success(Guid.NewGuid());
    public OperationResult<bool> DeleteScheme(Guid schemeGuid) => OperationResult<bool>.Success(true);
    public OperationResult<bool> WriteSchemeText(Guid schemeGuid, string name, string description) => OperationResult<bool>.Success(true);
}
