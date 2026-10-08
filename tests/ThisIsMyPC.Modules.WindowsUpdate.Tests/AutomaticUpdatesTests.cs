using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public sealed class AutomaticUpdatesTests
{
    [Theory]
    [InlineData(null, "0", "Enabled")]
    [InlineData(0, "0", "Enabled")]
    [InlineData(1, "1", "Disabled")]
    public async Task OptionsPreserveSavedStateAndUndo(int? current, string selected, string label)
    {
        var registry = new FakeRegistryService();
        registry.SetString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Professional");
        var capabilities = new CapabilityDetector(registry);
        if (current is { } existing) registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate", existing);
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions", 2);
        var policies = new PolicyControlStateReader(registry, () => [Source(current)]);
        var setting = new AutomaticUpdatesSetting(registry, capabilities, policies);
        var card = setting.CreateCard();
        Assert.Equal(new[] { "Enabled", "Disabled" }, card.Model.AvailableOptions!.Select(o => o.DisplayName));
        Assert.Equal(selected, card.Model.CurrentValue);
        Assert.Equal(label, card.Model.CurrentDisplayValue);
        var next = selected == "0" ? "1" : "0";
        var change = Assert.Single(setting.Create(next).Changes);
        var before = new LocalPolicyValue(current?.ToString(), current?.ToString() ?? "");
        Assert.Equal(before, LocalPolicyValue.Decode(change.BeforeValue));
        Assert.True(AutomaticUpdatesSetting.Allows(change));
        var service = new RecordingService(before);
        var module = new WindowsUpdateModule(registry, service);
        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.Equal(new LocalPolicyValue(next, next), service.Current);
        Assert.True((await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(before, service.Current);
        Assert.Equal(2, registry.ReadDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions").Value);
        var inspector = new WindowsUpdateSetEntryInspector(registry, capabilities, policies);
        Assert.True(inspector.Inspect(new() { ModuleId = "Windows Update", SettingId = AutomaticUpdatesSetting.Id, Value = selected, Description = "" })!.IsApplied);
        Assert.NotNull(inspector.CreateChangeGroup(new() { ModuleId = "Windows Update", SettingId = AutomaticUpdatesSetting.Id, Value = next, Description = "" }));
    }

    [Fact]
    public void UnreadableMismatchedAndInvalidPoliciesBlockChanges()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate", 1);
        var source = Source(0);
        var setting = new AutomaticUpdatesSetting(registry, policies: new(registry, () => [source]));
        Assert.True(setting.CreateCard().ReadPolicyState!().BlocksChanges);
        Assert.Throws<InvalidOperationException>(() => setting.Create("0"));
        source = Source(1) with { Status = PolicyFileStatus.Unreadable };
        Assert.True(setting.CreateCard().ReadPolicyState!().BlocksChanges);
        source = Source(1);
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate", 2);
        Assert.True(setting.CreateCard().ReadPolicyState!().BlocksChanges);
        Assert.Throws<ArgumentException>(() => setting.Create("2"));
    }

    [Fact]
    public async Task HomeCannotCreateAChangeAndMissingWriterFails()
    {
        var registry = new FakeRegistryService();
        registry.SetString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Core");
        var setting = new AutomaticUpdatesSetting(registry, new CapabilityDetector(registry), new(registry, () => [Source(null)]));
        Assert.Throws<InvalidOperationException>(() => setting.Create("1"));
        registry.SetString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Professional");
        var capabilities = new CapabilityDetector(registry);
        Assert.Throws<InvalidOperationException>(() => new AutomaticUpdatesSetting(registry, capabilities).Create("1"));
        var change = new AutomaticUpdatesSetting(registry, capabilities, new(registry, () => [Source(null)])).Create("1").Changes.Single();
        Assert.Equal(ErrorCategory.ServiceUnavailable, (await new WindowsUpdateModule(registry).ApplyChangeAsync(change)).ErrorCategory);
    }

    private static PolicySourceSnapshot Source(int? value) => new("Computer", PolicyScope.Machine,
        value is null ? PolicyFileStatus.Missing : PolicyFileStatus.Loaded,
        value is { } current ? [new(WindowsUpdateRegistryPaths.AuPoliciesKeyPath[5..], "NoAutoUpdate", 4, [checked((byte)current), 0, 0, 0])] : []);

    private sealed class RecordingService(LocalPolicyValue initial) : ILocalPolicyService
    {
        public LocalPolicyValue Current { get; private set; } = initial;
        public OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
        {
            Assert.Equal(AutomaticUpdatesSetting.Location, location);
            Assert.Equal(Current, before);
            Current = after;
            return OperationResult<bool>.Success(true);
        }
    }
}
