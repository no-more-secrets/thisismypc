using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Integration.Tests.Fakes;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>Story 10.5: Home tab dashboard.</summary>
public sealed class HomeViewModelTests
{
    private static SystemIdentity Identity() => new()
    {
        MachineName = "TEST-PC",
        WindowsEdition = "Windows 11 Education",
        WindowsVersion = "24H2 (build 26200)",
        Cpu = "Test CPU",
        Gpu = "Test GPU",
        Ram = "32 GB",
        Manufacturer = "Test Manufacturer",
        Model = "Test Model",
        SystemType = "64-bit operating system, x64-based processor",
    };

    private static ChangeHistoryEntry Entry(long id, string? groupId, string name, DateTimeOffset at) => new()
    {
        Id = id,
        ModuleId = "Test",
        SettingId = $"s{id}",
        DisplayName = name,
        SystemLocation = @"HKCU\Test",
        BeforeValue = "0",
        AfterValue = "1",
        ValueType = ChangeValueType.Registry_DWord,
        GroupId = groupId,
        AppliedAt = at,
    };

    [Fact]
    public async Task RecentActivity_GroupsBatches_TakesLatestFive()
    {
        var now = DateTimeOffset.Now;
        var entries = new List<ChangeHistoryEntry>
        {
            Entry(1, "g1", "Copilot", now),
            Entry(2, "g1", "Copilot", now),
        };
        for (var i = 3; i <= 9; i++)
            entries.Add(Entry(i, null, $"Setting {i}", now.AddMinutes(-i)));

        var vm = new HomeViewModel(Identity(), new FakeChangeHistoryServiceWithEntries(entries));
        await vm.LoadRecentActivityCommand.ExecuteAsync(null);

        Assert.Equal(5, vm.RecentActivityGroups.SelectMany(group => group.Items).Count());
        Assert.True(vm.HasRecentActivity);
        var first = vm.RecentActivityGroups.SelectMany(group => group.Items).First();
        Assert.Equal("Copilot", first.DisplayName);
        Assert.Equal("Test", first.ModuleDisplay);
        Assert.Equal("2 operations", first.OperationCountDisplay);
        Assert.Equal("Applied", first.StatusDisplay);
        Assert.Equal(2, first.Details.Count);
        Assert.All(first.Details, detail => Assert.Equal("0 to 1", detail.ChangeDisplay));
    }

    [Fact]
    public async Task NoHistory_EmptyStateExposed()
    {
        var vm = new HomeViewModel(Identity(), new FakeChangeHistoryService());
        await vm.LoadRecentActivityCommand.ExecuteAsync(null);

        Assert.Empty(vm.RecentActivityGroups);
        Assert.False(vm.HasRecentActivity);
    }

}
