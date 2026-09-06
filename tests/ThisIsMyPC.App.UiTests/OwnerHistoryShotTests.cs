using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.App.UiTests;

public sealed class OwnerHistoryShotTests
{
    [AvaloniaFact]
    public async Task Imported_records_show_restrictions_without_mutation_actions()
    {
        var vm = new ChangeHistoryViewModel(null!, _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(), null!, () => throw new InvalidOperationException());
        var entry = new ChangeHistoryEntryViewModel
        {
            Id = 1, DisplayName = "Show file extensions", ModuleId = "Windows Annoyances",
            SystemLocation = @"HKCU\Software\Example", BeforeDisplay = "Hidden", AfterDisplay = "Shown",
            Category = ChangeCategory.SystemReversion, AppliedAt = DateTimeOffset.Now, IsReverted = false,
            CanExecuteHistoryAction = false,
            ActionRestriction = "Owner Mode record. Restore and custom sets are unavailable for this record.",
        };
        var batch = new HistoryBatchViewModel
        {
            DisplayName = entry.DisplayName, BeforeDisplay = entry.BeforeDisplay, AfterDisplay = entry.AfterDisplay,
            Category = entry.Category, AppliedAt = entry.AppliedAt, IsReverted = false, GroupId = "owner-1",
            Details = [entry], SourceEntries = [new ChangeHistoryEntry
            {
                Id = 1, ModuleId = entry.ModuleId, SettingId = "test", DisplayName = entry.DisplayName,
                SystemLocation = entry.SystemLocation, BeforeValue = "1", AfterValue = "0",
                AppliedAt = entry.AppliedAt, ValueType = ChangeValueType.Registry_DWord, OwnerAttemptId = Guid.NewGuid(),
            }],
        };
        var group = new ChangeHistoryGroupViewModel { DateHeader = "Today" };
        group.Batches.Add(batch);
        vm.HistoryGroups.Add(group);
        vm.TotalGroupCount = 1;
        vm.DisplayedGroupCount = 1;
        using var session = UiSession.ForView(new ChangeHistoryView(), vm, "owner-history");
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            session.Pump();
            Assert.Empty(session.FindAll<Button>(b => b.Content is "Restore" or "Redo"));
            Assert.False(session.Find<CheckBox>(_ => true).IsEnabled);
            Assert.True(session.IsTextVisible(entry.ActionRestriction));
            session.Screenshot(theme.Key.ToString()!);
        }
        await vm.RestoreCommand.ExecuteAsync(entry);
        Assert.Equal(entry.ActionRestriction, vm.ErrorMessage);
        await vm.RedoCommand.ExecuteAsync(entry);
        Assert.Equal(entry.ActionRestriction, vm.ErrorMessage);
    }
}