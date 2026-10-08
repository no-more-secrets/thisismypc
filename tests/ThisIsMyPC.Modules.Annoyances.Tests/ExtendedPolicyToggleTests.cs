using System.Collections.Immutable;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class ExtendedPolicyToggleTests
{
    public static TheoryData<string> Settings => new()
    {
        "windows-tips", "consumer-features", "silent-app-installs", "welcome-experience", "app-suggestions",
        "settings-suggested-content", "tailored-experiences", "advertising-id", "feedback-frequency", "dynamic-search-box",
        "game-dvr", "spotlight-features", "spotlight-collection-desktop", "edge-sidebar", "edge-shortcuts", "bing-search", "copilot",
        "activity-history", "edge-debloat", "preinstalled-apps",
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public async Task EverySupportedControl_StagesAppliesAndUndoesPolicyAndPreferences(string id)
    {
        var registry = new FakeRegistryService();
        var targets = LocalPolicyToggleCatalog.Targets("Windows Annoyances", id);
        using var machine = new Session(false);
        using var account = new Session(true);
        var inherited = new Session(true);
        foreach (var target in targets)
        {
            var split = target.Location.LastIndexOf('\\');
            registry.WriteDWord(target.Location[..split], target.Location[(split + 1)..], int.Parse(target.Suppressed));
            var session = target.Location.StartsWith("HKCU", StringComparison.Ordinal) ? inherited : machine;
            session.WriteSaved(target.Location, ChangeValueType.LocalPolicy_DWord, new(target.Suppressed, target.Suppressed));
            session.Save();
        }
        IReadOnlyList<PolicySourceSnapshot> Sources() => [machine.ReadSource(), inherited.ReadSource() with { IsAccountPolicy = false }, account.ReadSource()];
        var policies = new PolicyControlStateReader(registry, Sources);
        var reader = new AnnoyancesSettingsReader(registry);
        var scan = new AnnoyancesScanData(reader.ReadAll(), reader.ReadBingSearch(), reader.ReadSettingsSuggestedContent(), reader.ReadCopilotPolicy(),
            reader.ReadRecall(), reader.ReadLockScreenAds(), reader.ReadPreinstalledApps(), reader.ReadEdgeDebloat(), reader.ReadActivityHistory());
        var card = new AnnoyancesCardProvider(reader).BuildCards(scan).Single(c => c.Model.SettingId == id);
        var raw = card.CreateToggleGroup(false);
        var entry = new SetEntry { ModuleId = "Windows Annoyances", SettingId = id, Value = raw.Changes[0].AfterValue!, Description = "Allow" };
        var inspector = new AnnoyancesSetEntryInspector(registry, policies);
        Assert.False(policies.Read("Windows Annoyances", id).BlocksChanges);
        var group = inspector.CreateChangeGroup(entry)!;
        Assert.NotNull(group);
        var writes = group.Changes.Where(c => LocalPolicyValue.IsPolicyType(c.ValueType)).ToArray();
        Assert.Equal(targets.Count, writes.Length);
        Assert.All(writes, change => Assert.True(LocalPolicyToggleCatalog.Allows(change)));
        new PendingChangesService(policyStates: policies).Stage(group);
        var savedMachine = machine.Entries;
        var savedInherited = inherited.Entries;
        var module = new AnnoyancesModule(registry, new Service(machine, account, registry));
        foreach (var change in group.Changes)
        {
            var result = await module.ApplyChangeAsync(change);
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }
        Assert.True(inspector.Inspect(entry)!.IsApplied);
        foreach (var change in group.Changes.Reverse())
        {
            var result = await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue });
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }
        Assert.Equal(RegistryPolicyFile.Serialize(savedMachine.OrderBy(e => e.ValueName)), RegistryPolicyFile.Serialize(machine.Entries.OrderBy(e => e.ValueName)));
        Assert.Equal(RegistryPolicyFile.Serialize(savedInherited), RegistryPolicyFile.Serialize(inherited.Entries));
        Assert.Empty(account.Entries);
    }

    [Fact]
    public void AccountPolicy_DoesNotTreatInheritedValueAsAccountBeforeState()
    {
        var registry = new FakeRegistryService();
        var target = LocalPolicyToggleCatalog.Targets("Windows Annoyances", "app-suggestions")[0];
        var key = target.Location[..target.Location.LastIndexOf('\\')];
        registry.WriteDWord(key, "DisableThirdPartySuggestions", 1);
        var common = new PolicySourceSnapshot("Common", PolicyScope.User, PolicyFileStatus.Loaded,
            [new(key[5..], "DisableThirdPartySuggestions", 4, [1, 0, 0, 0])]);
        var account = new PolicySourceSnapshot("Account", PolicyScope.User, PolicyFileStatus.Missing, []) { IsAccountPolicy = true };
        var policies = new PolicyControlStateReader(registry, () => [common, account]);
        var group = new AnnoyancesSetEntryInspector(registry, policies).CreateChangeGroup(new()
            { ModuleId = "Windows Annoyances", SettingId = "app-suggestions", Value = "1", Description = "Allow" })!;
        var policy = group.Changes.Single(c => LocalPolicyValue.IsPolicyType(c.ValueType));
        Assert.Equal(new LocalPolicyValue(null, "1"), LocalPolicyValue.Decode(policy.BeforeValue));
        Assert.Equal(new LocalPolicyValue("0", "0"), LocalPolicyValue.Decode(policy.AfterValue));
        common = common with { Status = PolicyFileStatus.Unreadable };
        Assert.True(policies.Read("Windows Annoyances", "app-suggestions").BlocksChanges);
    }

    [Fact]
    public void MixedDirectGroup_RemainsPartialInPresetPreview()
    {
        var registry = new FakeRegistryService();
        var targets = LocalPolicyToggleCatalog.Targets("Windows Annoyances", "activity-history");
        var entries = targets.Select((target, index) =>
        {
            var split = target.Location.LastIndexOf('\\');
            var value = index == 0 ? 0 : 1;
            registry.WriteDWord(target.Location[..split], target.Location[(split + 1)..], value);
            return new RegistryPolicyEntry(target.Location[5..split], target.Location[(split + 1)..], 4, BitConverter.GetBytes(value).ToImmutableArray());
        }).ToImmutableArray();
        var policies = new PolicyControlStateReader(registry, () => [new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded, entries)]);
        Assert.Null(policies.Read("Windows Annoyances", "activity-history").ToggleState);
        var state = new AnnoyancesSetEntryInspector(registry, policies).Inspect(new()
            { ModuleId = "Windows Annoyances", SettingId = "activity-history", Value = "", Description = "Default" })!;
        Assert.False(state.IsApplied);
        Assert.Equal("Partially set", state.CurrentDisplay);
    }

    [Fact]
    public void BroaderSpotlightPolicy_RemainsVisibleAndHasItsOwnControl()
    {
        var registry = new FakeRegistryService();
        var key = @"HKCU\SOFTWARE\Policies\Microsoft\Windows\CloudContent";
        registry.WriteDWord(key, "DisableWindowsSpotlightFeatures", 1);
        var common = new PolicySourceSnapshot("Common", PolicyScope.User, PolicyFileStatus.Loaded,
            [new(key[5..], "DisableWindowsSpotlightFeatures", 4, [1, 0, 0, 0])]);
        var account = new PolicySourceSnapshot("Account", PolicyScope.User, PolicyFileStatus.Missing, []) { IsAccountPolicy = true };
        var policies = new PolicyControlStateReader(registry, () => [common, account]);
        Assert.Contains("Windows Spotlight", policies.Read("Windows Annoyances", "welcome-experience").Message);
        Assert.True(policies.Read("Windows Annoyances", "spotlight-features").ToggleState);
        Assert.False(policies.Read("Windows Annoyances", "spotlight-features").BlocksChanges);
    }

    private sealed class Service(Session machine, Session account, IRegistryService registry) : ILocalPolicyService
    {
        public OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after) =>
            LocalPolicyTransaction.Apply(location.StartsWith("HKCU", StringComparison.Ordinal) ? account : machine, registry, location, type, before, after);
    }

    private sealed class Session(bool user) : ILocalPolicySession
    {
        public ImmutableArray<RegistryPolicyEntry> Entries { get; private set; } = [];
        private ImmutableArray<RegistryPolicyEntry> _staged;
        public PolicySourceSnapshot ReadSource() => new("Test", user ? PolicyScope.User : PolicyScope.Machine, PolicyFileStatus.Loaded, Entries)
            { IsAccountPolicy = user };
        public void WriteSaved(string location, ChangeValueType type, LocalPolicyValue value)
        {
            var split = location.LastIndexOf('\\');
            var key = location[5..split];
            var name = location[(split + 1)..];
            _staged = Entries.Where(e => !e.KeyPath.Equals(key, StringComparison.OrdinalIgnoreCase)
                || !e.ValueName.Equals(name, StringComparison.OrdinalIgnoreCase) && !e.ValueName.Equals("**del." + name, StringComparison.OrdinalIgnoreCase)).ToImmutableArray();
            if (value.Delete) _staged = _staged.Add(new(key, "**del." + name, 1, [32, 0, 0, 0]));
            else if (value.Saved is not null) _staged = _staged.Add(new(key, name, 4, BitConverter.GetBytes(int.Parse(value.Saved)).ToImmutableArray()));
        }
        public void Save() => Entries = _staged;
        public void Dispose() { }
    }
}
