using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Privacy;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Privacy.Tests.Fakes;

namespace ThisIsMyPC.Modules.Privacy.Tests;

public sealed class PrivacySetEntryInspectorTests
{
    private readonly FakeRegistryService _registry = new();

    private PrivacySetEntryInspector Inspector => new(_registry);

    private static SetEntry Entry(string settingId, string value) => new()
    {
        ModuleId = "Privacy & Telemetry",
        SettingId = settingId,
        Value = value,
        Description = "d",
    };

    [Fact]
    public void UnknownSettingId_ReturnsNull()
    {
        Assert.Null(Inspector.Inspect(Entry("no-such-setting", "1")));
        Assert.Null(Inspector.CreateChangeGroup(Entry("no-such-setting", "1")));
    }

    [Fact]
    public void Single_ConfigureEntry_ResolvesWithEnforcement()
    {
        var state = Inspector.Inspect(Entry("telemetry-level", "1"));
        Assert.NotNull(state);
        Assert.Equal("Windows default", state!.CurrentDisplay);
        Assert.False(state.IsApplied);

        var group = Inspector.CreateChangeGroup(Entry("telemetry-level", "1"));
        var change = Assert.Single(group!.Changes);
        Assert.Equal("1", change.AfterValue);
        Assert.Equal(["DiagTrack"], change.Enforcement!.CompanionServices);
    }

    [Fact]
    public void Single_EmptyValue_RestoresPolicyToNotConfigured()
    {
        _registry.SetDWord(PrivacyRegistryPaths.ErrorReportingPoliciesKeyPath, "Disabled", 1);

        var group = Inspector.CreateChangeGroup(Entry("error-reporting", ""));

        var change = Assert.Single(group!.Changes);
        Assert.Equal("", change.AfterValue);
        Assert.Equal("1", change.BeforeValue);
        Assert.Null(change.Enforcement);
    }

    [Fact]
    public void InkingTypingGroup_InspectsAndStagesAllFourValues()
    {
        var state = Inspector.Inspect(Entry("inking-typing", "1"));
        Assert.NotNull(state);
        Assert.False(state!.IsApplied);

        _registry.SetDWord(PrivacyRegistryPaths.InputPersonalizationKeyPath, "RestrictImplicitInkCollection", 1);
        var partial = Inspector.Inspect(Entry("inking-typing", "1"));
        Assert.Equal("Partially set", partial!.CurrentDisplay);

        var group = Inspector.CreateChangeGroup(Entry("inking-typing", "1"));
        Assert.NotNull(group);
        Assert.Equal(4, group!.Changes.Count);
        Assert.All(group.Changes, c => Assert.Equal("inking-typing", c.SettingId));
    }

    [Fact]
    public void BogusValue_MatchingNeitherDirection_IsNeverApplied_AndStagesNothing()
    {
        Assert.False(Inspector.Inspect(Entry("app-launch-tracking", "7"))!.IsApplied);
        Assert.Null(Inspector.CreateChangeGroup(Entry("app-launch-tracking", "7")));
    }
}
