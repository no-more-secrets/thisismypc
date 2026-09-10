using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Microsoft.Data.Sqlite;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Data;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

public sealed class OwnerServiceHistoryShotTests
{
    [AvaloniaFact]
    public async Task ServiceHistoryIsVisibleWithoutLocalMutationActions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tipc-owner-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var history = new ChangeHistoryService(new ChangeHistoryRepository(), Path.Combine(directory, "history.db"));
            await history.InitializeAsync();
            var row = new ChangeHistoryEntry
            {
                Id = 1, OwnerAttemptId = Guid.NewGuid(), ModuleId = "Windows Annoyances", SettingId = "test",
                DisplayName = "Start menu suggestions", SystemLocation = "HKCU\\Software\\Example\\Suggestions",
                BeforeDisplay = "Shown", AfterDisplay = "Hidden", BeforeValue = "1", AfterValue = "0",
                ValueType = ChangeValueType.Registry_DWord, AppliedAt = DateTimeOffset.Now,
                JournalOutcome = "Applied", JournalDetail = "Restored and verified.",
            };
            var vm = new ChangeHistoryViewModel(history, _ => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException(), null!, ownerHistory: () =>
                    Task.FromResult<IReadOnlyList<ChangeHistoryEntry>>([row, row, row with { OwnerAttemptId = null }]));
            await vm.LoadHistoryCommand.ExecuteAsync(null);
            Assert.Equal(1, vm.DisplayedGroupCount);
            Assert.Equal(1, vm.TotalGroupCount);
            var batch = Assert.Single(Assert.Single(vm.HistoryGroups).Batches);
            Assert.False(batch.CanCreateCustomSet);
            Assert.All(batch.SourceEntries, e => Assert.False(e.CanExecuteHistoryAction));
            using var session = UiSession.ForView(new ChangeHistoryView(), vm, "owner-service-history");
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                session.SetTheme(theme);
                session.Pump();
                Assert.True(session.IsTextVisible(row.DisplayName));
                Assert.Empty(session.FindAll<Button>(b => b.Content is "Restore" or "Redo"));
                session.Screenshot(theme.Key.ToString()!);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
