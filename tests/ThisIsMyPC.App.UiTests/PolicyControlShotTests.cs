using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Models;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.App.UiTests;

public class PolicyControlShotTests
{
    [AvaloniaFact]
    public void ExplorerSearch_ShowsPolicyChoiceWithoutStaging()
    {
        var registry = new UiFakeRegistryService();
        registry.SetDWord(@"HKLM\Software\Policies\Microsoft\Windows\Windows Search", "SearchOnTaskbarMode", 0);
        var queue = new PendingChangesService();
        using var vm = new ShellViewModel(new ShellScanData([], new TaskbarSettings(1, true, false, false)), queue, registry);
        var choice = vm.TaskbarChoiceSettings.Single(row => row.SystemPath.EndsWith("SearchboxTaskbarMode", StringComparison.Ordinal));
        vm.SearchText = choice.Label;
        using var session = UiSession.ForView(new ShellView(), vm, "policy-controls", width: 900, height: 650);
        session.ClickText("Taskbar");
        session.Pump();
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            Assert.False(choice.IsControlEnabled);
            Assert.Equal(0, choice.SelectedOption!.Value);
            Assert.True(session.IsTextVisible(choice.PolicyStateText!));
            Assert.False(choice.HasPendingChange);
            Assert.Empty(queue.PendingGroups);
            session.Screenshot($"explorer-search-{theme.Key}");
        }
    }

    [AvaloniaFact]
    public async Task PowerPolicy_DisablesOnlyThePluggedInEditor()
    {
        var registry = new UiFakeRegistryService();
        var setting = Guid.Parse("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
        registry.SetDWord($@"HKLM\Software\Policies\Microsoft\Power\PowerSettings\{setting:D}", "ACSettingIndex", 1200);
        var queue = new PendingChangesService();
        using var vm = new PowerViewModel(new PowerScanData([
            new PowerPlan { PlanGuid = Guid.NewGuid(), Name = "Balanced", Description = "Balanced plan", IsActive = true },
        ]), queue, registryService: registry, powerService: new UiFakePowerService([
            new PowerSettingInfo(Guid.NewGuid(), "Sleep", setting, "Sleep after", null, 600, 300, "Seconds", true, 0, 3600, 1, []),
        ]));
        using var session = UiSession.ForView(new PowerView(), vm, "policy-controls", width: 900, height: 650);
        await vm.OpenSettingsCommand.ExecuteAsync(vm.Plans[0]);
        session.Pump();
        var row = Assert.Single(Assert.Single(vm.SettingsGroups).Settings);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            Assert.False(row.CanEditAc);
            Assert.True(row.CanEditDc);
            Assert.True(session.IsTextVisible(row.PolicyStateText!));
            Assert.Empty(queue.PendingGroups);
            session.Screenshot($"power-ac-policy-{theme.Key}");
        }
    }
}
