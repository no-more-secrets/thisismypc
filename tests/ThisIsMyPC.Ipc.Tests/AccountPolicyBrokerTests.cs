using ThisIsMyPC.Broker;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class AccountPolicyBrokerTests
{
    [Fact]
    public void AccountPolicy_RequiresTheSameAdministratorAccount()
    {
        var target = LocalPolicyToggleCatalog.Targets("Windows Annoyances", "app-suggestions")[0];
        var change = Change("app-suggestions", target);
        Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
        Assert.True(PrivilegedModuleHost.CanApplyProtectedChoice(change, "S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1001"));
        Assert.False(PrivilegedModuleHost.CanApplyProtectedChoice(change, "S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1002"));
        Assert.False(PrivilegedModuleHost.CanApplyProtectedChoice(change, "S-1-5-21-1-2-3-1001", null));
        Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { SystemLocation = change.SystemLocation.Replace("HKCU", "HKU\\S-1-5-21-1-2-3-1002", StringComparison.Ordinal) }] }).IsSuccess);
    }

    [Fact]
    public void ExpandedPolicyTargets_RejectOtherSettingsAndScopes()
    {
        foreach (var id in new[] { "windows-tips", "consumer-features", "silent-app-installs", "welcome-experience", "app-suggestions",
            "settings-suggested-content", "tailored-experiences", "advertising-id", "feedback-frequency", "dynamic-search-box",
            "game-dvr", "spotlight-features", "spotlight-collection-desktop", "edge-sidebar", "edge-shortcuts", "bing-search", "copilot",
            "activity-history", "edge-debloat", "preinstalled-apps" })
        foreach (var target in LocalPolicyToggleCatalog.Targets("Windows Annoyances", id))
        {
            var change = Change(id, target);
            Assert.True(BrokerRequestPolicy.Create(new() { Changes = [change] }).IsSuccess);
            Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { SettingId = "unsupported" }] }).IsSuccess);
            Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { AfterValue = new LocalPolicyValue("2", "2").Encode() }] }).IsSuccess);
            Assert.False(BrokerRequestPolicy.Create(new() { Changes = [change with { SystemLocation = target.Location + "Other" }] }).IsSuccess);
        }
    }

    private static ChangeDescriptor Change(string id, PolicyToggleTarget target) => new()
    {
        ModuleId = "Windows Annoyances", SettingId = id, DisplayName = id, SystemLocation = target.Location,
        ValueType = ChangeValueType.LocalPolicy_DWord, BeforeValue = new LocalPolicyValue(null, target.Suppressed).Encode(),
        AfterValue = new LocalPolicyValue(target.Allowed, target.Allowed).Encode(), BeforeDisplay = "Suppressed", AfterDisplay = "Allowed",
        Enforcement = new SettingEnforcement { SkuRestriction = target.Edition },
    };
}
