using System.Text;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Power.Changes;
using ThisIsMyPC.Modules.Power.Services;
using ThisIsMyPC.Modules.Power.Tests.Fakes;

namespace ThisIsMyPC.Modules.Power.Tests;

public sealed class PowerPolicySettingsTests
{
    private static readonly Guid Plan = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SavedPoliciesCanBeRemovedAndRestored(bool sleep)
    {
        var registry = new FakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Professional");
        registry.SetDWord(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath, "ACSettingIndex", 0);
        registry.SetDWord(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath, "DCSettingIndex", 1);
        registry.WriteString(PowerPlanChangeFactory.ActivePlanPolicyKeyPath, "ActivePowerScheme", Plan.ToString("D"));
        var sources = new[] { Source() };
        var reader = new PolicyControlStateReader(registry, () => sources);
        var editor = new PowerPolicySettings(registry, reader);
        Assert.False(editor.State(sleep).BlocksChanges);
        Assert.Null(editor.State(sleep).Message);
        var group = sleep ? editor.CreateSleep(true) : editor.CreatePin(Plan, false);
        Assert.Equal(sleep ? 2 : 1, group.Changes.Count);
        var service = new RecordingPolicyService();
        var module = new PowerModule(new FakePowerService(), registry, service);
        foreach (var change in group.Changes)
        {
            Assert.True(PowerPolicySettings.Allows(change));
            Assert.Equal(new LocalPolicyValue(null, ""), LocalPolicyValue.Decode(change.AfterValue));
            Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
            Assert.Equal(LocalPolicyValue.Decode(change.AfterValue), service.Current);
            Assert.True((await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
            Assert.Equal(LocalPolicyValue.Decode(change.BeforeValue), service.Current);
        }
        Assert.Equal(sleep ? "0" : Plan.ToString("D"), LocalPolicyValue.Decode(group.Changes[0].BeforeValue)!.Saved);
        sources[0] = Source() with { Status = PolicyFileStatus.Unreadable };
        Assert.True(editor.State(sleep).BlocksChanges);
        Assert.Throws<InvalidOperationException>(() => sleep ? editor.CreateSleep(true) : editor.CreatePin(Plan, false));
    }

    [Fact]
    public void MismatchUnknownValuesAndHomeCannotBeEdited()
    {
        var registry = new FakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Professional");
        var editor = new PowerPolicySettings(registry, new(registry, () => [Source()]));
        Assert.True(editor.State(true).BlocksChanges);
        Assert.True(editor.State(false).BlocksChanges);
        registry.SetDWord(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath, "ACSettingIndex", 2);
        Assert.Throws<InvalidOperationException>(() => editor.CreateSleep(false));
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Core");
        Assert.NotNull(editor.UnavailableReason);
        Assert.Throws<InvalidOperationException>(() => editor.CreatePin(Plan, false));
    }

    [Fact]
    public void AbsentLiveOnlyAndDeletionStatesRemainExact()
    {
        var registry = new FakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "Professional");
        registry.SetDWord(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath, "ACSettingIndex", 0);
        var source = new PolicySourceSnapshot("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath[5..], "**Del.DCSettingIndex", 1, [32, 0, 0, 0])]);
        var editor = new PowerPolicySettings(registry, new(registry, () => [source]));
        var changes = editor.CreateSleep(true).Changes;
        Assert.Equal(new LocalPolicyValue(null, "0"), LocalPolicyValue.Decode(changes[0].BeforeValue));
        Assert.Equal(new LocalPolicyValue(null, "", true), LocalPolicyValue.Decode(changes[1].BeforeValue));
        Assert.All(changes, c => Assert.True(PowerPolicySettings.Allows(c)));
        Assert.False(PowerPolicySettings.Allows(changes[0] with { SystemLocation = PowerPolicySettings.PinLocation }));
        Assert.False(PowerPolicySettings.Allows(changes[0] with { AfterValue = new LocalPolicyValue("2", "2").Encode() }));
    }

    private static PolicySourceSnapshot Source() => new("Computer", PolicyScope.Machine, PolicyFileStatus.Loaded,
    [
        new(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath[5..], "ACSettingIndex", 4, [0, 0, 0, 0]),
        new(PowerPlanChangeFactory.AllowStandbyPolicyKeyPath[5..], "DCSettingIndex", 4, [1, 0, 0, 0]),
        new(PowerPlanChangeFactory.ActivePlanPolicyKeyPath[5..], "ActivePowerScheme", 1, [.. Encoding.Unicode.GetBytes(Plan.ToString("D") + "\0")]),
    ]);

    private sealed class RecordingPolicyService : ILocalPolicyService
    {
        public LocalPolicyValue? Current { get; private set; }
        public OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
        {
            Current = after;
            return OperationResult<bool>.Success(true);
        }
    }
}
