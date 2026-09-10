using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Hardware;

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
            Model = "Unknown",
            SystemType = "64-bit operating system, x64-based processor",
        };
        using var viewModel = new HomeViewModel(identity, history, hardwareDetection: new ShotHardware());
        await viewModel.LoadHardwareAsync();
        await viewModel.LoadRecentActivityCommand.ExecuteAsync(null);

        using var session = UiSession.ForView(new HomeView(), viewModel, "home-overview", width: 976, height: 676);
        session.Screenshot("dark-expanded-content");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("light-expanded-content");

        Assert.True(session.IsTextVisible("Windows 11 Education"));
        Assert.True(session.IsTextVisible("64 GB"));
        Assert.True(session.IsTextVisible("B550"));
        Assert.True(session.IsTextVisible("Inferred from motherboard model"));
        Assert.True(session.IsTextVisible("Windows 11 to Windows 10"));
        Assert.False(session.IsTextVisible("Quick Actions"));
    }

    private sealed class ShotHardware : IHardwareDetectionService
    {
        public Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HardwareSnapshot
        {
            Facts = new() { Identity = MachineIdentity.From("ASUSTeK COMPUTER INC.", null), FormFactor = new() { SmbiosChassisTypes = [3] } },
            Firmware = new() { BoardManufacturer = "ASUSTeK COMPUTER INC.", BoardProduct = "ROG STRIX B550-F GAMING (WI-FI)", BiosVersion = "3636", BiosDate = "07/18/2025", MemoryDevices = [new("DIMM_A2", 32UL * 1024 * 1024 * 1024, "DDR4", 3200)] },
            Chipset = new("B550", "Inferred from motherboard model"),
            Devices = [new("NVIDIA GeForce RTX 4080", "Display", []), new("Samsung SSD 990 PRO 2TB", "DiskDrive", []), new("WD_BLACK SN850X 4000GB", "DiskDrive", [])],
        });
        public Task<HardwareSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => GetSnapshotAsync(cancellationToken);
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
