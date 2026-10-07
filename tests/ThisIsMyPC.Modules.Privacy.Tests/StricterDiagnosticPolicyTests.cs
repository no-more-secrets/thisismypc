using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Modules.Privacy.Services;
using ThisIsMyPC.Modules.Privacy.Tests.Fakes;

namespace ThisIsMyPC.Modules.Privacy.Tests;

public sealed class StricterDiagnosticPolicyTests
{
    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    public void PresetRecognizesARequiredOrStricterPolicy(int value, bool applied, bool covered)
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(PrivacyRegistryPaths.DataCollectionPoliciesKeyPath, "AllowTelemetry", value);
        var inspector = new PrivacySetEntryInspector(registry);
        var entry = new SetEntry { ModuleId = inspector.ModuleId, SettingId = "telemetry-level", Value = "1", Description = "Limit data" };
        var state = inspector.Inspect(entry)!;
        Assert.Equal(applied, state.IsApplied);
        Assert.Equal(covered, state.CoveredByPolicy is not null);
        Assert.Equal(applied, new PrivacySettingsReader(registry).ReadSingles().Single(p => p.Id == "telemetry-level").IsConfigured);
        // Detection preserves the real before-value for explicit changes and undo.
        var change = Assert.Single(inspector.CreateChangeGroup(entry)!.Changes);
        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), change.BeforeValue);
        Assert.False(inspector.Inspect(entry with { Value = "" })!.IsApplied);
        Assert.Null(inspector.Inspect(entry with { Value = "" })!.CoveredByPolicy);
    }
}
