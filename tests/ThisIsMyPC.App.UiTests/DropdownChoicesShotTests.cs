using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class DropdownChoicesShotTests
{
    [AvaloniaFact]
    public void UpdateModesRevealFieldsAndDiscardCompleteGroup()
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(ThisIsMyPC.Modules.WindowsUpdate.WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "EditionID", "Professional");
        var queue = new PendingChangesService();
        using var vm = new WindowsUpdateViewModel(new WindowsUpdateSettingsReader(registry).ReadAll(), queue, registry, capabilityDetector: new CapabilityDetector(registry));
        using var session = UiSession.ForView(new SettingCardPageView(), vm, "dropdown-choices", width: 1000, height: 800);
        var install = vm.CardGroups.SelectMany(group => group.Cards).Single(card => card.Model.SettingId == "auto-update-mode");
        session.Click(session.Find<ComboBox>(box => ReferenceEquals(box.DataContext, install)));
        session.ClickText("Scheduled installation");
        Assert.Equal(3, Assert.Single(queue.PendingGroups).Changes.Count);
        Assert.True(session.IsTextVisible("Install time"));
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot("scheduled-" + theme.Key);
        }
        queue.Unstage(Assert.Single(queue.PendingGroups).GroupId);
        session.Pump();
        Assert.Equal("", install.SelectedOption!.Value);
        Assert.False(session.IsTextVisible("Install time"));

        session.ClickText("Delivery Optimization");
        var sharing = vm.CardGroups.SelectMany(group => group.Cards).Single(card => card.Model.SettingId == "delivery-optimization");
        session.Click(session.Find<ComboBox>(box => ReferenceEquals(box.DataContext, sharing)));
        session.Screenshot("sharing-options");
        session.ClickText("Private group");
        Assert.Equal("2", Assert.Single(queue.PendingGroups).Changes[0].AfterValue);
        queue.Unstage(Assert.Single(queue.PendingGroups).GroupId);

        session.ClickText("Update Experience");
        var hours = vm.CardGroups.SelectMany(group => group.Cards).Single(card => card.Model.SettingId == "active-hours-manual");
        session.Click(session.Find<ComboBox>(box => ReferenceEquals(box.DataContext, hours)));
        session.ClickText("Manual");
        Assert.Equal(3, Assert.Single(queue.PendingGroups).Changes.Count);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot("manual-hours-" + theme.Key);
        }
        // An intermediate invalid range must remain editable, with no stale valid group queued.
        hours.ChoiceFields[0].SelectedOption = hours.ChoiceFields[0].Options.Single(option => option.Value == "20");
        Assert.Empty(queue.PendingGroups);
        Assert.NotNull(hours.ChoiceError);
        hours.ChoiceFields[1].SelectedOption = hours.ChoiceFields[1].Options.Single(option => option.Value == "7");
        Assert.Null(hours.ChoiceError);
        Assert.Equal(["2", "20", "7"], Assert.Single(queue.PendingGroups).Changes.Select(change => change.AfterValue));
        using var revisited = new WindowsUpdateViewModel(new WindowsUpdateSettingsReader(registry).ReadAll(), queue, registry);
        var restored = revisited.CardGroups.SelectMany(group => group.Cards).Single(card => card.Model.SettingId == "active-hours-manual");
        Assert.Equal("20", restored.ChoiceFields[0].SelectedOption!.Value);
        queue.Unstage(Assert.Single(queue.PendingGroups).GroupId);
        session.Pump();
        Assert.Equal("1", hours.SelectedOption!.Value);
        Assert.Equal("8", hours.ChoiceFields[0].SelectedOption!.Value);
    }

    [AvaloniaFact]
    public async Task ExplorerAndTaskbarChoicesStageExactValues()
    {
        var registry = new UiFakeRegistryService();
        var queue = new PendingChangesService();
        using var vm = new ShellViewModel(new ShellScanData(new ExplorerSettingsReader(registry).ReadAll(), new TaskbarSettings(1, true, false, false)), queue, registry);
        using var session = UiSession.ForView(new ShellView(), vm, "dropdown-shell", width: 1000, height: 850);
        session.ClickText("File Explorer");
        var destination = Assert.Single(vm.FileExplorerChoiceSettings);
        session.Click(session.Find<ComboBox>(box => ReferenceEquals(box.DataContext, destination)));
        session.ClickText("This PC");
        await session.WaitForAsync(() => queue.PendingGroups.Count == 1);
        var change = Assert.Single(Assert.Single(queue.PendingGroups).Changes);
        Assert.Equal("", change.BeforeValue);
        Assert.Equal("1", change.AfterValue);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot("destination-" + theme.Key);
        }
        queue.Unstage(Assert.Single(queue.PendingGroups).GroupId);
        session.ClickText("Taskbar");
        var alignment = vm.TaskbarChoiceSettings.Single(row => row.Label == "Taskbar alignment");
        session.Click(session.Find<ComboBox>(box => ReferenceEquals(box.DataContext, alignment)));
        session.ClickText("Left");
        await session.WaitForAsync(() => queue.PendingGroups.Count == 1);
        Assert.Equal("0", Assert.Single(queue.PendingGroups).Changes[0].AfterValue);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Screenshot("alignment-" + theme.Key);
        }
    }
}
