using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Privacy.Tests.Fakes;

namespace ThisIsMyPC.Modules.Privacy.Tests;

public sealed class DiagnosticDataTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("")]
    public async Task ModuleAppliesAndUndoesBothSavedAndLiveState(string next)
    {
        var registry = Registry("Enterprise");
        registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", 1);
        var service = new RecordingPolicyService(new("1", "1"));
        var policies = new PolicyControlStateReader(registry, () => [Source(PolicyScope.Machine, 1), NoUserPolicy]);
        var change = new DiagnosticDataSetting(registry, new CapabilityDetector(registry), policies).Create(next).Changes.Single();
        var module = new PrivacyModule(registry, service, policies);
        Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        Assert.Equal(next, service.Current.Live);
        Assert.Equal(next == "" ? null : next, service.Current.Saved);
        Assert.Equal(next == "", service.Current.Delete);
        Assert.True((await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(new LocalPolicyValue("1", "1"), service.Current);
    }

    private sealed class RecordingPolicyService(LocalPolicyValue initial) : ILocalPolicyService
    {
        public LocalPolicyValue Current { get; private set; } = initial;
        public Core.Results.OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
        {
            Assert.Equal(DiagnosticDataSetting.Location, location);
            Assert.Equal(ChangeValueType.LocalPolicy_DWord, type);
            Assert.Equal(Current, before);
            Current = after;
            return Core.Results.OperationResult<bool>.Success(true);
        }
    }

    [Theory]
    [InlineData("Professional", false)]
    [InlineData("Enterprise", true)]
    [InlineData("Education", true)]
    [InlineData("Core", false)]
    public void OptionsFollowEditionAndRejectOffOnPro(string edition, bool off)
    {
        var registry = Registry(edition);
        var setting = new DiagnosticDataSetting(registry, new CapabilityDetector(registry));
        Assert.Equal(off, setting.Options.Any(o => o.Value == "0"));
        if (!off) Assert.Throws<ArgumentException>(() => setting.Create("0"));
        if (edition == "Core") Assert.Throws<InvalidOperationException>(() => setting.Create("1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void SavedPolicyCapturesExactValuesAndExportsOptions(int oldValue)
    {
        var registry = Registry("Enterprise");
        registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", oldValue);
        var source = Source(PolicyScope.Machine, oldValue);
        var policies = new PolicyControlStateReader(registry, () => [source, NoUserPolicy]);
        var setting = new DiagnosticDataSetting(registry, new CapabilityDetector(registry), policies);
        foreach (var option in setting.Options)
        {
            var change = Assert.Single(setting.Create(option.Value).Changes);
            Assert.True(DiagnosticDataSetting.Allows(change));
            var before = LocalPolicyValue.Decode(change.BeforeValue)!;
            Assert.Equal(oldValue.ToString(System.Globalization.CultureInfo.InvariantCulture), before.Live);
            Assert.Equal(before.Live, before.Saved);
            var after = LocalPolicyValue.Decode(change.AfterValue)!;
            Assert.Equal(option.Value, after.Live);
            Assert.Equal(option.Value == "", after.Delete);
            Assert.Null(change.Enforcement!.CompanionServices);
            Assert.True(DiagnosticDataSetting.Allows(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue }));
        }
        var inspector = new PrivacySetEntryInspector(registry, new CapabilityDetector(registry), policies);
        Assert.NotNull(inspector.CreateChangeGroup(new() { ModuleId = "Privacy & Telemetry", SettingId = "telemetry-level", Value = "3", Description = "" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompetingUserPolicyBlocksStaging(bool unreadable)
    {
        var registry = Registry("Enterprise");
        registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", 1);
        registry.SetDWord("HKCU" + PrivacyRegistryPaths.DataCollectionPoliciesKeyPath[4..], "AllowTelemetry", 0);
        var user = unreadable ? new PolicySourceSnapshot("User", PolicyScope.User, PolicyFileStatus.Unreadable, [], "Denied")
            : Source(PolicyScope.User, 1); // Saved value differs from live zero.
        var policies = new PolicyControlStateReader(registry, () => [Source(PolicyScope.Machine, 1), user]);
        var setting = new DiagnosticDataSetting(registry, new CapabilityDetector(registry), policies);
        Assert.True(setting.CreateCard().ReadPolicyState!().BlocksChanges);
        Assert.Throws<InvalidOperationException>(() => setting.Create("3"));
    }

    [Fact]
    public async Task UserPolicyAddedAfterStagingBlocksApply()
    {
        var registry = Registry("Enterprise");
        registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", 1);
        var policies = new PolicyControlStateReader(registry, () => [Source(PolicyScope.Machine, 1), NoUserPolicy]);
        var change = new DiagnosticDataSetting(registry, new CapabilityDetector(registry), policies).Create("3").Changes.Single();
        registry.SetDWord("HKCU" + PrivacyRegistryPaths.DataCollectionPoliciesKeyPath[4..], "AllowTelemetry", 0);
        var result = await new PrivacyModule(registry, policies: policies).ApplyChangeAsync(change);
        Assert.False(result.IsSuccess);
        Assert.Contains("user policy", result.ErrorMessage);
        Assert.Equal(1, registry.ReadDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry").Value);
    }

    [Fact]
    public void InvalidValuesAndLocationsAreRejected()
    {
        var registry = Registry("Enterprise");
        registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", 1);
        var setting = new DiagnosticDataSetting(registry, new CapabilityDetector(registry),
            new PolicyControlStateReader(registry, () => [Source(PolicyScope.Machine, 1), NoUserPolicy]));
        var change = setting.Create("0").Changes.Single();
        Assert.False(DiagnosticDataSetting.Allows(change with { SystemLocation = DiagnosticDataSetting.Location + "Other" }));
        Assert.False(DiagnosticDataSetting.Allows(change with { AfterValue = new LocalPolicyValue("2", "2").Encode() }));
        registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", 2);
        Assert.True(setting.CreateCard().ReadPolicyState!().BlocksChanges);
    }

    private static readonly PolicySourceSnapshot NoUserPolicy = new("User", PolicyScope.User, PolicyFileStatus.Missing, []);

    private static FakeRegistryService Registry(string edition)
    {
        var registry = new FakeRegistryService();
        registry.SetString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        return registry;
    }

    private static PolicySourceSnapshot Source(PolicyScope scope, int value) => new("Local policy", scope, PolicyFileStatus.Loaded,
        [new(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath[5..], "AllowTelemetry", 4, [checked((byte)value), 0, 0, 0])]);
}
