using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Integration.Tests.Sets;

/// <summary>
/// Validates the bundled built-in set JSON (copied to output from
/// src/ThisIsMyPC.App/sets/) against the real module change factories, so a renamed
/// settingId, wrong module id, or wrong-polarity value breaks CI instead of silently
/// producing a dead set entry. Pure file loads — no live system access, so no
/// Integration category.
/// </summary>
public sealed class BuiltInSetsTests
{
    private readonly SetLoadResult _result;

    public BuiltInSetsTests()
    {
        var builtInDir = Path.Combine(AppContext.BaseDirectory, "sets");
        var userDir = Path.Combine(AppContext.BaseDirectory, "no-such-user-sets");
        _result = new SetProvider(builtInDir, userDir).LoadSets();
    }

    /// <summary>
    /// The desired value per (moduleId, settingId), derived by running the real module
    /// factories in the debloat direction (toggle value convention: group toggles use
    /// the group's first descriptor). Nothing here restates the JSON — ids and values
    /// come from the factories, so drift in either direction fails.
    /// </summary>
    private static Dictionary<(string ModuleId, string SettingId), string> BuildDesiredValues()
    {
        var desired = new Dictionary<(string, string), string>();

        var reader = new AnnoyancesSettingsReader(new FakeRegistryService());
        foreach (var pref in reader.ReadAll())
        {
            var change = AnnoyanceChangeFactory.CreateToggle(pref, suppress: true);
            desired[(change.ModuleId, change.SettingId)] = change.AfterValue!;
        }

        var copilot = AnnoyanceChangeFactory.CreateCopilotPolicyToggle(reader.ReadCopilotPolicy(), suppress: true);
        desired[(copilot.Changes[0].ModuleId, copilot.Changes[0].SettingId)] = copilot.Changes[0].AfterValue!;

        var recall = AnnoyanceChangeFactory.CreateGroupToggle(
            reader.ReadRecall(), "recall", "Recall", "d", suppress: true);
        desired[(recall.Changes[0].ModuleId, recall.Changes[0].SettingId)] = recall.Changes[0].AfterValue!;

        var suggested = AnnoyanceChangeFactory.CreateGroupToggle(
            reader.ReadSettingsSuggestedContent(), "settings-suggested-content", "Suggested", "d", suppress: true);
        desired[(suggested.Changes[0].ModuleId, suggested.Changes[0].SettingId)] = suggested.Changes[0].AfterValue!;

        var bing = AnnoyanceChangeFactory.CreateBingSearchToggle(reader.ReadBingSearch(), suppress: true);
        desired[(bing.Changes[0].ModuleId, bing.Changes[0].SettingId)] = bing.Changes[0].AfterValue!;

        var taskbar = new TaskbarSettings(
            Alignment: 1, WidgetsEnabled: true, ClassicContextMenu: false, ClassicCommandBar: false);
        foreach (var change in new[]
                 {
                     TaskbarChangeFactory.CreateAlignmentChange(taskbar, newAlignment: 0),
                     TaskbarChangeFactory.CreateWidgetsToggle(taskbar, enable: false),
                     TaskbarChangeFactory.CreateClassicContextMenuToggle(taskbar, enable: true),
                     TaskbarChangeFactory.CreateCommandBarToggle(taskbar, enable: true),
                 })
        {
            desired[(change.ModuleId, change.SettingId)] = change.AfterValue!;
        }

        return desired;
    }

    [Fact]
    public void BundledSets_LoadCleanly()
    {
        Assert.Empty(_result.Warnings);
        Assert.Equal(
            ["NukeCopilot", "Privacy Baseline", "Windows 10-ify"],
            _result.Sets.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(_result.Sets, s => Assert.Equal(SetSource.BuiltIn, s.Source));
    }

    [Fact]
    public void EveryEntry_TargetsARealModuleSetting_WithTheFactoryDesiredValue()
    {
        var desired = BuildDesiredValues();

        foreach (var set in _result.Sets)
        {
            foreach (var entry in set.Entries)
            {
                Assert.True(
                    desired.TryGetValue((entry.ModuleId, entry.SettingId), out var expectedValue),
                    $"{set.Name}: unknown setting ({entry.ModuleId}, {entry.SettingId})");
                Assert.True(
                    string.Equals(entry.Value, expectedValue, StringComparison.Ordinal),
                    $"{set.Name}/{entry.SettingId}: value '{entry.Value}' != factory desired value '{expectedValue}'");
            }
        }
    }

    [Fact]
    public void BuiltInEntries_CarryNoEnforcement_ModuleFactoriesAreAuthoritative()
    {
        Assert.All(
            _result.Sets.SelectMany(s => s.Entries),
            entry => Assert.Null(entry.Enforcement));
    }

    [Fact]
    public void Windows10Ify_IsAGroupedOptimizationPack_IncludingNukeCopilot()
    {
        var pack = _result.Sets.Single(s => s.Name == "Windows 10-ify");

        Assert.Equal(SetCategory.OptimizationPack, pack.Category);
        Assert.All(pack.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Group)));
        Assert.Contains("NukeCopilot", pack.Entries.Select(e => e.Group).Distinct());

        // The NukeCopilot group mirrors the standalone tweak set entry-for-entry
        var standalone = _result.Sets.Single(s => s.Name == "NukeCopilot");
        Assert.Equal(SetCategory.TweakSet, standalone.Category);
        Assert.Equal(
            standalone.Entries.Select(e => (e.ModuleId, e.SettingId, e.Value)),
            pack.Entries.Where(e => e.Group == "NukeCopilot").Select(e => (e.ModuleId, e.SettingId, e.Value)));
    }
}
