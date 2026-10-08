using System.Collections.Immutable;
using System.Text;
using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Modules.Security.Tests;

public sealed class LocalPolicyTests
{
    [Fact]
    public void SaveGuard_DoesNotAdoptAnotherEditorsUnrelatedChanges()
    {
        var target = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps").Targets[0];
        var original = RegistryPolicyFile.Serialize([new("Other", "Value", 4, [0, 0, 0, 0])]);
        var loadedHive = new LocalPolicySaveGuard(original);
        loadedHive.Stage(target.Location, ChangeValueType.LocalPolicy_DWord, new("1", "1"));
        var persisted = RegistryPolicyFile.Serialize([new("Other", "Value", 4, [2, 0, 0, 0]),
            new(target.Key[5..], target.Name, 4, [1, 0, 0, 0])]);
        Assert.Throws<InvalidOperationException>(() => loadedHive.AcceptSaved(persisted));
        Assert.Equal(original, loadedHive.Expected);
    }

    [Fact]
    public void SaveGuard_AcceptsOwnChangeWithUnrelatedValuesIntact()
    {
        var target = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps").Targets[0];
        var unrelated = new RegistryPolicyEntry("Other", "Value", 4, [0, 0, 0, 0]);
        var guard = new LocalPolicySaveGuard(RegistryPolicyFile.Serialize([unrelated]));
        guard.Stage(target.Location, ChangeValueType.LocalPolicy_DWord, new("1", "1"));
        var actual = RegistryPolicyFile.Serialize([new(target.Key[5..], target.Name, 4, [1, 0, 0, 0]), unrelated]);
        guard.AcceptSaved(actual);
        Assert.Equal(actual, guard.Expected);
    }

    [Theory]
    [MemberData(nameof(SecurityTests.Options), MemberType = typeof(SecurityTests))]
    public async Task AllChoices_RestoreSavedAndLiveValuesAndExport(string settingId, string optionId)
    {
        if (settingId == "secure-sign-in") return;
        var registry = new UiFakeRegistryService();
        using var session = new Session();
        var policies = new PolicyControlStateReader(registry, () => [session.ReadSource()]);
        var reader = new SecuritySettings(registry, policies);
        var service = new Service(session, registry);
        var module = new SecurityModule(registry, service, policies);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == settingId);
        var option = setting.Choices.Single(o => o.Id == optionId);
        var original = setting.Choices.First(o => o.Id != optionId && o.Id != "default");
        foreach (var change in reader.Create(setting, original).Changes)
            Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        var saved = RegistryPolicyFile.Serialize(session.Entries.OrderBy(e => e.ValueName, StringComparer.Ordinal));
        var group = reader.Create(setting, option);
        Assert.All(group.Changes, c => Assert.True(SecurityCatalog.Allows(c)));
        var encoder = new SecuritySetValueEncoder();
        Assert.Equal(optionId, encoder.Encode(settingId, group.Changes.Select(c => new SetValue(c.SystemLocation, c.ValueType, c.AfterValue!)).ToList()));
        foreach (var change in group.Changes) Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.Equal(optionId, reader.Read(setting).Option?.Id);
        foreach (var change in group.Changes.Reverse())
            Assert.True((await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(saved, RegistryPolicyFile.Serialize(session.Entries.OrderBy(e => e.ValueName, StringComparer.Ordinal)));
        Assert.Equal(original.Id, reader.Read(setting).Option?.Id);
    }

    [Fact]
    public void DeleteInstruction_RoundTripsWithoutTouchingUnrelatedPolicy()
    {
        var registry = new UiFakeRegistryService();
        using var session = new Session();
        var target = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps").Targets[0];
        session.Entries = [new("Unrelated", "Value", 4, [9, 0, 0, 0]),
            new(target.Key[5..], "**del." + target.Name, 1, [32, 0, 0, 0])];
        var original = RegistryPolicyFile.Serialize(session.Entries);
        var before = new LocalPolicyValue(null, "", true);
        var after = new LocalPolicyValue("2", "2");
        Assert.True(LocalPolicyTransaction.Apply(session, registry, target.Location, ChangeValueType.LocalPolicy_DWord, before, after).IsSuccess);
        Assert.True(LocalPolicyTransaction.Apply(session, registry, target.Location, ChangeValueType.LocalPolicy_DWord, after, before).IsSuccess);
        Assert.Equal(original, RegistryPolicyFile.Serialize(session.Entries));
    }

    [Fact]
    public void SaveFailure_RestoresPriorPolicyAndLiveValue()
    {
        var registry = new UiFakeRegistryService();
        using var session = new Session { FailNextSave = true };
        var target = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps").Targets[0];
        registry.WriteDWord(target.Key, target.Name, 0);
        var result = LocalPolicyTransaction.Apply(session, registry, target.Location, ChangeValueType.LocalPolicy_DWord,
            new(null, "0"), new("1", "1"));
        Assert.False(result.IsSuccess);
        Assert.Empty(session.Entries);
        Assert.Equal("0", ((IRegistryService)registry).ReadValue(target.Key, target.Name).Value!.Data);
    }

    [Fact]
    public void ChangedSavedValue_RefusesBeforeWriting()
    {
        var registry = new UiFakeRegistryService();
        using var session = new Session();
        var target = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps").Targets[0];
        session.Entries = [new(target.Key[5..], target.Name, 4, [2, 0, 0, 0])];
        var result = LocalPolicyTransaction.Apply(session, registry, target.Location, ChangeValueType.LocalPolicy_DWord,
            new(null, ""), new("1", "1"));
        Assert.False(result.IsSuccess);
        Assert.Equal(0, session.Writes);
    }

    [Theory]
    [InlineData("**DelVals.")]
    [InlineData("**DeleteValues")]
    [InlineData("**DeleteKeys")]
    public void BroadDirectives_RemainBlocked(string directive)
    {
        var registry = new UiFakeRegistryService();
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps");
        using var session = new Session { Entries = [new(setting.Targets[0].Key[5..], directive, 1, [32, 0, 0, 0])] };
        var reader = new SecuritySettings(registry, new PolicyControlStateReader(registry, () => [session.ReadSource()]));
        Assert.NotNull(reader.Read(setting).BlockReason);
        Assert.Throws<InvalidOperationException>(() => reader.Create(setting, setting.Choices[1]));
    }

    [Fact]
    public void RejectedLiveWrite_RestoresSavedPolicy()
    {
        var registry = new FaultRegistry();
        using var session = new Session();
        var target = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps").Targets[0];
        var result = LocalPolicyTransaction.Apply(session, registry, target.Location, ChangeValueType.LocalPolicy_DWord,
            new(null, ""), new("1", "1"));
        Assert.False(result.IsSuccess);
        Assert.Contains("previous policy values were restored", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(session.Entries);
    }

    [Fact]
    public void ExternalWriteDuringSave_IsNotOverwritten()
    {
        var registry = new UiFakeRegistryService();
        var target = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps").Targets[0];
        using var session = new Session { AfterSave = () => registry.WriteDWord(target.Key, target.Name, 2) };
        var result = LocalPolicyTransaction.Apply(session, registry, target.Location, ChangeValueType.LocalPolicy_DWord,
            new(null, ""), new("1", "1"));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.Equal(2, registry.ReadDWord(target.Key, target.Name).Value);
    }

    [Fact]
    public void QueueRechecksSavedState_AndOldDirectDescriptorCannotBypassIt()
    {
        var registry = new UiFakeRegistryService();
        using var session = new Session();
        var policies = new PolicyControlStateReader(registry, () => [session.ReadSource()]);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps");
        var target = setting.Targets[0];
        var change = Assert.Single(new SecuritySettings(registry, policies).Create(setting, setting.Choices[1]).Changes);
        var raw = Assert.Single(new SecuritySettings(registry).Create(setting, setting.Choices[1]).Changes);
        session.Entries = [new(target.Key[5..], target.Name, 4, [0, 0, 0, 0])];
        Assert.True(policies.Read(change).BlocksChanges);
        Assert.True(policies.Read(raw).BlocksChanges);
    }

    [Fact]
    public void BrokerCatalog_RejectsInvalidSavedPayload()
    {
        var registry = new UiFakeRegistryService();
        using var session = new Session();
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "unwanted-apps");
        var reader = new SecuritySettings(registry, new PolicyControlStateReader(registry, () => [session.ReadSource()]));
        var change = Assert.Single(reader.Create(setting, setting.Choices[1]).Changes);
        Assert.False(SecurityCatalog.Allows(change with { AfterValue = new LocalPolicyValue("999", "0").Encode() }));
        Assert.False(SecurityCatalog.Allows(change with { AfterValue = "{}" }));
        Assert.False(SecurityCatalog.Allows(change with { SystemLocation = "HKLM\\SOFTWARE\\Other\\Value" }));
    }

    private sealed class Service(Session session, IRegistryService registry) : ILocalPolicyService
    {
        public OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
            => LocalPolicyTransaction.Apply(session, registry, location, type, before, after);
    }

    private sealed class Session : ILocalPolicySession
    {
        public ImmutableArray<RegistryPolicyEntry> Entries { get; set; } = [];
        private ImmutableArray<RegistryPolicyEntry> _staged;
        public bool FailNextSave { get; set; }
        public Action? AfterSave { get; init; }
        public int Writes { get; private set; }
        public PolicySourceSnapshot ReadSource() => new("Local computer policy", PolicyScope.Machine, PolicyFileStatus.Loaded, Entries);
        public void WriteSaved(string location, ChangeValueType type, LocalPolicyValue value)
        {
            Writes++;
            var split = location.LastIndexOf('\\');
            var key = location[5..split];
            var name = location[(split + 1)..];
            _staged = Entries.Where(e => e.KeyPath != key || e.ValueName != name && e.ValueName != "**del." + name).ToImmutableArray();
            if (value.Delete) _staged = _staged.Add(new(key, "**del." + name, 1, [32, 0, 0, 0]));
            else if (value.Saved is not null)
                _staged = _staged.Add(new(key, name, type == ChangeValueType.LocalPolicy_String ? 1u : 4u,
                    (type == ChangeValueType.LocalPolicy_String ? Encoding.Unicode.GetBytes(value.Saved + '\0')
                        : BitConverter.GetBytes(int.Parse(value.Saved, System.Globalization.CultureInfo.InvariantCulture))).ToImmutableArray()));
        }
        public void Save()
        {
            Entries = _staged;
            AfterSave?.Invoke();
            if (FailNextSave) { FailNextSave = false; throw new IOException("Simulated save failure after writing."); }
        }
        public void Dispose() { }
    }

    private sealed class FaultRegistry : IRegistryService
    {
        private readonly IRegistryService _inner = new UiFakeRegistryService();
        public OperationResult<int> ReadDWord(string key, string name) => _inner.ReadDWord(key, name);
        public OperationResult<string> ReadString(string key, string name) => _inner.ReadString(key, name);
        public OperationResult<string> ReadExpandString(string key, string name) => _inner.ReadExpandString(key, name);
        public OperationResult<string[]> ReadMultiString(string key, string name) => _inner.ReadMultiString(key, name);
        public OperationResult<byte[]> ReadBinary(string key, string name) => _inner.ReadBinary(key, name);
        public OperationResult<bool> WriteDWord(string key, string name, int value) => OperationResult<bool>.Failure("Write rejected", ErrorCategory.AccessDenied);
        public OperationResult<bool> WriteString(string key, string name, string value) => throw new NotSupportedException();
        public OperationResult<bool> WriteExpandString(string key, string name, string value) => throw new NotSupportedException();
        public OperationResult<bool> WriteMultiString(string key, string name, string[] values) => throw new NotSupportedException();
        public OperationResult<bool> WriteBinary(string key, string name, byte[] value) => throw new NotSupportedException();
        public OperationResult<bool> DeleteValue(string key, string name) => _inner.DeleteValue(key, name);
        public OperationResult<bool> DeleteKey(string key, bool recursive = false) => throw new NotSupportedException();
        public OperationResult<bool> KeyExists(string key) => _inner.KeyExists(key);
        public OperationResult<bool> ValueExists(string key, string name) => _inner.ValueExists(key, name);
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string key) => _inner.EnumerateSubKeys(key);
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string key) => _inner.EnumerateValues(key);
        public OperationResult<string> ReadValueBeforeWrite(string key, string name) => _inner.ReadValueBeforeWrite(key, name);
    }
}
