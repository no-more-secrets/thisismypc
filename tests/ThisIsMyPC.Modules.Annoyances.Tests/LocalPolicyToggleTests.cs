using System.Collections.Immutable;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

public sealed class LocalPolicyToggleTests
{
    private const string Module = "Windows Annoyances";

    [Theory]
    [InlineData("windows-tips", "DisableSoftLanding", "1", 2)]
    [InlineData("silent-app-installs", "DisableWindowsConsumerFeatures", "1", 2)]
    [InlineData("consumer-features", "DisableWindowsConsumerFeatures", "0", 1)]
    public void SavedPolicy_IsEditableAndPreservesBothBeforeValues(string id, string name, string off, int count)
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(LocalPolicyToggleCatalog.CloudKey, name, 1);
        var source = Source(name, 1);
        var policies = new PolicyControlStateReader(registry, () => [source]);
        var inspector = new AnnoyancesSetEntryInspector(registry, policies);
        var state = policies.Read(Module, id);
        Assert.False(state.BlocksChanges);
        Assert.True(state.ToggleState);
        var entry = Entry(id, off);
        Assert.False(inspector.Inspect(entry)!.IsApplied);
        var group = inspector.CreateChangeGroup(entry)!;
        Assert.Equal(count, group.Changes.Count);
        var policy = Assert.Single(group.Changes.Where(c => LocalPolicyValue.IsPolicyType(c.ValueType)));
        Assert.Equal(new LocalPolicyValue("1", "1"), LocalPolicyValue.Decode(policy.BeforeValue));
        Assert.Equal(new LocalPolicyValue("0", "0"), LocalPolicyValue.Decode(policy.AfterValue));
        Assert.True(LocalPolicyToggleCatalog.Allows(policy));
        Assert.True(LocalPolicyToggleCatalog.Allows(policy with { BeforeValue = policy.AfterValue!, AfterValue = policy.BeforeValue }));
        var pending = new PendingChangesService(policyStates: policies);
        pending.Stage(group);
        registry.SetDWord(LocalPolicyToggleCatalog.CloudKey, name, 0);
        Assert.True(policies.Read(policy).BlocksChanges);
    }

    [Fact]
    public void OrdinaryPreference_DoesNotIntroduceConsumerPolicy()
    {
        var registry = new FakeRegistryService();
        var policies = new PolicyControlStateReader(registry, () => [new("Computer", PolicyScope.Machine, PolicyFileStatus.Missing, [])]);
        var inspector = new AnnoyancesSetEntryInspector(registry, policies);
        var group = inspector.CreateChangeGroup(Entry("silent-app-installs", "0"))!;
        Assert.Equal(ChangeValueType.Registry_DWord, Assert.Single(group.Changes).ValueType);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    public void ConflictingOrUnknownValues_RemainBlocked(int saved, int live)
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(LocalPolicyToggleCatalog.CloudKey, "DisableSoftLanding", live);
        var policies = new PolicyControlStateReader(registry, () => [Source("DisableSoftLanding", saved)]);
        Assert.True(policies.Read(Module, "windows-tips").BlocksChanges);
        var inspector = new AnnoyancesSetEntryInspector(registry, policies);
        var group = inspector.CreateChangeGroup(Entry("windows-tips", "1"))!;
        Assert.Throws<InvalidOperationException>(() => new PendingChangesService(policyStates: policies).Stage(group));
    }

    [Fact]
    public void BroadDeletionAndUnreadableSources_RemainBlocked()
    {
        var registry = new FakeRegistryService();
        var source = new PolicySourceSnapshot("Computer", PolicyScope.Machine, PolicyFileStatus.Unreadable, []);
        var policies = new PolicyControlStateReader(registry, () => [source]);
        Assert.True(policies.Read(Module, "consumer-features").BlocksChanges);
        source = new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(LocalPolicyToggleCatalog.CloudKey[5..], "**DelVals", 1, [32, 0, 0, 0])]);
        Assert.True(policies.Read(Module, "consumer-features").BlocksChanges);
    }

    [Fact]
    public void SharedPolicy_CannotBeQueuedByTwoControls()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(LocalPolicyToggleCatalog.CloudKey, "DisableWindowsConsumerFeatures", 1);
        var policies = new PolicyControlStateReader(registry, () => [Source("DisableWindowsConsumerFeatures", 1)]);
        var inspector = new AnnoyancesSetEntryInspector(registry, policies);
        var pending = new PendingChangesService(policyStates: policies);
        pending.Stage(inspector.CreateChangeGroup(Entry("consumer-features", "0"))!);
        Assert.Throws<InvalidOperationException>(() => pending.Stage(inspector.CreateChangeGroup(Entry("silent-app-installs", "1"))!));
        Assert.Single(pending.PendingGroups);
    }

    [Theory]
    [InlineData("windows-tips", "DisableSoftLanding", "1")]
    [InlineData("silent-app-installs", "DisableWindowsConsumerFeatures", "1")]
    [InlineData("consumer-features", "DisableWindowsConsumerFeatures", "0")]
    public async Task ApplyAndUndo_RestoreSavedPolicyAndPreference(string id, string name, string off)
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(LocalPolicyToggleCatalog.CloudKey, name, 1);
        using var session = new Session { Source = Source(name, 1) };
        var policies = new PolicyControlStateReader(registry, () => [session.Source]);
        var group = new AnnoyancesSetEntryInspector(registry, policies).CreateChangeGroup(Entry(id, off))!;
        var module = new AnnoyancesModule(registry, new Service(session, registry));
        foreach (var change in group.Changes)
            Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.Equal(0, registry.ReadDWord(LocalPolicyToggleCatalog.CloudKey, name).Value);
        Assert.Equal(0, BitConverter.ToInt32(Assert.Single(session.Source.Entries).Data.AsSpan()));
        foreach (var change in group.Changes.Reverse())
            Assert.True((await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(1, registry.ReadDWord(LocalPolicyToggleCatalog.CloudKey, name).Value);
        Assert.Equal(1, BitConverter.ToInt32(Assert.Single(session.Source.Entries).Data.AsSpan()));
        Assert.True(policies.Read(Module, id).ToggleState);
    }

    private sealed class Service(Session session, IRegistryService registry) : ILocalPolicyService
    {
        public ThisIsMyPC.Core.Results.OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
            => LocalPolicyTransaction.Apply(session, registry, location, type, before, after);
    }

    private sealed class Session : ILocalPolicySession
    {
        public required PolicySourceSnapshot Source { get; set; }
        private ImmutableArray<RegistryPolicyEntry> _staged;
        public PolicySourceSnapshot ReadSource() => Source;
        public void WriteSaved(string location, ChangeValueType type, LocalPolicyValue value)
        {
            var split = location.LastIndexOf('\\');
            var key = location[5..split];
            var name = location[(split + 1)..];
            _staged = Source.Entries.Where(e => e.KeyPath != key || e.ValueName != name).ToImmutableArray();
            if (value.Saved is not null)
                _staged = _staged.Add(new(key, name, 4, BitConverter.GetBytes(int.Parse(value.Saved, System.Globalization.CultureInfo.InvariantCulture)).ToImmutableArray()));
        }
        public void Save() => Source = Source with { Entries = _staged };
        public void Dispose() { }
    }

    private static SetEntry Entry(string id, string value) => new() { ModuleId = Module, SettingId = id, Value = value, Description = "Test" };
    private static PolicySourceSnapshot Source(string name, int value) => new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
        [new(LocalPolicyToggleCatalog.CloudKey[5..], name, 4, BitConverter.GetBytes(value).ToImmutableArray())]);
}
