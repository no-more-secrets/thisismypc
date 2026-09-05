using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>Rendered Home overview at the content size of a 1196 by 800 expanded window.</summary>
public sealed class HomeShotTests
{
    [AvaloniaFact]
    public async Task Overview_FillsContent_AndShowsDetailedActivity_InBothThemes()
    {
        var history = new ShotHistoryService(
        [
            Entry(1, "Explorer", "Taskbar style", "Windows 11", "Windows 10", DateTimeOffset.Now),
            Entry(2, "Privacy & Telemetry", "Advertising ID", "On", "Off", DateTimeOffset.Now.AddMinutes(-12)),
            Entry(3, "Power Plans", "Sleep timeout", "30 minutes", "Never", DateTimeOffset.Now.AddDays(-1)),
        ]);
        var identity = new SystemIdentity
        {
            MachineName = "SAM-PC-W11-4080",
            WindowsEdition = "Windows 11 Education",
            WindowsVersion = "25H2 (OS build 26200.1234)",
            Cpu = "AMD Ryzen 9 5950X 16-Core Processor",
            Gpu = "NVIDIA GeForce RTX 4080",
            Ram = "64 GB",
            Manufacturer = "ASUSTeK COMPUTER INC.",
            Model = "ROG STRIX X570-E GAMING",
            SystemType = "64-bit operating system, x64-based processor",
        };
        var viewModel = new HomeViewModel(identity, history);
        await viewModel.LoadRecentActivityCommand.ExecuteAsync(null);

        using var session = UiSession.ForView(new HomeView(), viewModel, "home-overview", width: 976, height: 676);
        session.Screenshot("dark-expanded-content");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("light-expanded-content");

        Assert.True(session.IsTextVisible("Windows 11 Education"));
        Assert.True(session.IsTextVisible("64 GB"));
        Assert.True(session.IsTextVisible("Windows 11 to Windows 10"));
        Assert.False(session.IsTextVisible("Quick Actions"));
    }

    private static ChangeHistoryEntry Entry(long id, string moduleId, string name, string before, string after, DateTimeOffset at) => new()
    {
        Id = id,
        ModuleId = moduleId,
        SettingId = $"setting-{id}",
        DisplayName = name,
        SystemLocation = @"HKLM\SOFTWARE\Example",
        BeforeValue = before,
        AfterValue = after,
        BeforeDisplay = before,
        AfterDisplay = after,
        ValueType = ChangeValueType.Registry_String,
        Category = ChangeCategory.Modify,
        AppliedAt = at,
    };

    private sealed class ShotHistoryService(IReadOnlyList<ChangeHistoryEntry> entries) : IChangeHistoryService
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task RecordChangesAsync(MutationResult result) => Task.CompletedTask;
        public Task RecordDriftEventsAsync(IReadOnlyList<ChangeHistoryEntry> driftEntries) => Task.CompletedTask;
        public Task<IReadOnlyList<ChangeHistoryEntry>> GetHistoryAsync(int? limit = null, int? offset = null) => Task.FromResult(entries);
        public Task<IReadOnlyList<ChangeHistoryEntry>> GetRecentGroupedAsync(int groupLimit = 50) => Task.FromResult(entries);
        public Task<int> GetGroupCountAsync() => Task.FromResult(entries.Count);
        public Task<int> GetEntryCountAsync() => Task.FromResult(entries.Count);
        public Task ClearHistoryAsync() => Task.CompletedTask;
        public Task<OperationResult<bool>> RevertChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> revertFunc)
            => Task.FromResult(OperationResult<bool>.Success(true));
        public Task<OperationResult<bool>> RedoChangeAsync(long historyId, Func<ChangeDescriptor, Task<OperationResult<bool>>> applyFunc)
            => Task.FromResult(OperationResult<bool>.Success(true));
    }
}
