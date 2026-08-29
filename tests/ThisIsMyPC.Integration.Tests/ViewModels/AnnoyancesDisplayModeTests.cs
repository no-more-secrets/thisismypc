using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances;
using ThisIsMyPC.Modules.Annoyances.Models;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

/// <summary>Story 10.2: display modes on the card-rendered Annoyances tab.</summary>
public sealed class AnnoyancesDisplayModeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-dmvm-{Guid.NewGuid():N}");
    private readonly Fakes.FakeRegistryService _registry = new();
    private readonly PendingChangesService _pending = new();

    private string StorePath => Path.Combine(_dir, "display-modes.txt");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<AnnoyancesViewModel> CreateVmAsync(DisplayModePreferencesStore? store = null)
    {
        // The Integration.Tests fake registry is a stub; the Annoyances scanner reads
        // through it and sees every value as absent — fine for display-mode tests.
        var scan = await new AnnoyancesModule(_registry).ScanSystemStateAsync();
        return new AnnoyancesViewModel((AnnoyancesScanData)scan.Value!, _pending, _registry, store);
    }

    private static IEnumerable<SettingCardViewModel> AllCards(AnnoyancesViewModel vm)
        => vm.CardGroups.SelectMany(g => g.Cards);

    [Fact]
    public async Task DefaultMode_DescriptionsExpanded_RegistryDataHidden()
    {
        var vm = await CreateVmAsync();

        Assert.False(vm.ShowRegistryData);
        Assert.False(vm.IsCompact);
        Assert.All(AllCards(vm), c => Assert.True(c.IsDescriptionVisible));
        Assert.All(AllCards(vm), c => Assert.False(c.IsRegistryDataVisible));
    }

    [Fact]
    public async Task RegistryDataMode_ShowsRegistryDataOnEveryCard()
    {
        var vm = await CreateVmAsync();

        vm.ShowRegistryData = true;

        Assert.All(AllCards(vm), c => Assert.True(c.IsRegistryDataVisible));
        Assert.All(AllCards(vm), c => Assert.True(c.IsDescriptionVisible));
    }

    [Fact]
    public async Task CompactMode_CollapsesDescriptions()
    {
        var vm = await CreateVmAsync();

        vm.IsCompact = true;

        Assert.All(AllCards(vm), c => Assert.False(c.IsDescriptionVisible));
        Assert.All(AllCards(vm), c => Assert.False(c.IsRegistryDataVisible));
    }

    [Fact]
    public async Task BothMode_MaximumDensity()
    {
        var vm = await CreateVmAsync();

        vm.ShowRegistryData = true;
        vm.IsCompact = true;

        Assert.All(AllCards(vm), c => Assert.False(c.IsDescriptionVisible));
        Assert.All(AllCards(vm), c => Assert.True(c.IsRegistryDataVisible));
    }

    [Fact]
    public async Task ModeSwitch_MutatesExistingCards_PendingTintSurvives()
    {
        var vm = await CreateVmAsync();
        var card = AllCards(vm).First();
        card.IsEnabled = !card.IsEnabled;
        await Task.Delay(350); // debounce
        Assert.True(card.HasPendingChange);

        var before = AllCards(vm).ToList();
        vm.IsCompact = true;
        vm.ShowRegistryData = true;

        // Same VM instances (no rebuild) and the tint flags are untouched.
        Assert.Equal(before, AllCards(vm).ToList());
        Assert.True(card.HasPendingChange);
    }

    [Fact]
    public async Task ModeChanges_PersistPerTab_AndRestoreOnConstruction()
    {
        var store = new DisplayModePreferencesStore(StorePath);
        var vm = await CreateVmAsync(store);
        vm.ShowRegistryData = true;
        vm.IsCompact = true;

        var restored = await CreateVmAsync(new DisplayModePreferencesStore(StorePath));

        Assert.True(restored.ShowRegistryData);
        Assert.True(restored.IsCompact);
        Assert.All(AllCards(restored), c => Assert.False(c.IsDescriptionVisible));
        Assert.All(AllCards(restored), c => Assert.True(c.IsRegistryDataVisible));
    }

    [Fact]
    public async Task NullStore_ModesWorkInMemory_NothingWritten()
    {
        var vm = await CreateVmAsync(store: null);

        vm.ShowRegistryData = true;

        Assert.All(AllCards(vm), c => Assert.True(c.IsRegistryDataVisible));
        Assert.False(File.Exists(StorePath));
    }
}
