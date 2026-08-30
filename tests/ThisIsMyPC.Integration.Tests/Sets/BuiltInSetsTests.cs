using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.WindowsUpdate;
using ThisIsMyPC.Modules.WindowsUpdate.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Services;

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

        var recall = AnnoyanceChangeFactory.CreateRecallPolicyToggle(
            reader.ReadRecall(), suppress: true, "d");
        desired[(recall.Changes[0].ModuleId, recall.Changes[0].SettingId)] = recall.Changes[0].AfterValue!;

        var suggested = AnnoyanceChangeFactory.CreateGroupToggle(
            reader.ReadSettingsSuggestedContent(), "settings-suggested-content", "Suggested", "d", suppress: true);
        desired[(suggested.Changes[0].ModuleId, suggested.Changes[0].SettingId)] = suggested.Changes[0].AfterValue!;

        var lockScreen = AnnoyanceChangeFactory.CreateGroupToggle(
            reader.ReadLockScreenAds(), "lock-screen-ads", "Lock screen", "d", suppress: true);
        desired[(lockScreen.Changes[0].ModuleId, lockScreen.Changes[0].SettingId)] = lockScreen.Changes[0].AfterValue!;

        var preinstalled = AnnoyanceChangeFactory.CreateGroupToggle(
            reader.ReadPreinstalledApps(), "preinstalled-apps", "Preinstalled", "d", suppress: true);
        desired[(preinstalled.Changes[0].ModuleId, preinstalled.Changes[0].SettingId)] = preinstalled.Changes[0].AfterValue!;

        var activity = AnnoyanceChangeFactory.CreateActivityHistoryToggle(
            reader.ReadActivityHistory(), suppress: true, "d");
        desired[(activity.Changes[0].ModuleId, activity.Changes[0].SettingId)] = activity.Changes[0].AfterValue!;

        var bing = AnnoyanceChangeFactory.CreateBingSearchToggle(reader.ReadBingSearch(), suppress: true);
        desired[(bing.Changes[0].ModuleId, bing.Changes[0].SettingId)] = bing.Changes[0].AfterValue!;

        // Windows Update: singles from the reader (configured values are static), the
        // version-pin group from a registry seeded with a DisplayVersion (the group's
        // set value is its first descriptor — TargetReleaseVersion "1").
        var wuRegistry = new StoringFakeRegistryService();
        wuRegistry.WriteString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "DisplayVersion", "24H2");
        var wuReader = new WindowsUpdateSettingsReader(wuRegistry);
        foreach (var setting in wuReader.ReadSingles())
        {
            var change = WindowsUpdateChangeFactory.CreateToggle(setting, configure: true);
            desired[(change.ModuleId, change.SettingId)] = change.AfterValue!;
        }
        var versionPin = WindowsUpdateChangeFactory.CreateVersionPinGroup(wuReader.ReadVersionPin(), configure: true)!;
        desired[(versionPin.Changes[0].ModuleId, versionPin.Changes[0].SettingId)] = versionPin.Changes[0].AfterValue!;

        var privacyReader = new ThisIsMyPC.Modules.Privacy.Services.PrivacySettingsReader(new FakeRegistryService());
        foreach (var pref in privacyReader.ReadSingles())
        {
            var change = ThisIsMyPC.Modules.Privacy.Changes.PrivacyChangeFactory.CreateToggle(pref, configure: true);
            desired[(change.ModuleId, change.SettingId)] = change.AfterValue!;
        }
        var inking = ThisIsMyPC.Modules.Privacy.Changes.PrivacyChangeFactory.CreateInkingTypingGroup(
            privacyReader.ReadInkingTyping(), configure: true, "d");
        desired[(inking.Changes[0].ModuleId, inking.Changes[0].SettingId)] = inking.Changes[0].AfterValue!;

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

    /// <summary>
    /// The Startup &amp; Services module uses instance-scoped settingIds
    /// (service-starttype:X, scheduled-task:P), so its desired values are derived per
    /// entry by round-tripping the settingId through the real factory in the debloat
    /// direction. Null = the settingId doesn't match the factory's format.
    /// </summary>
    private static string? StartupFactoryDesiredValue(SetEntry entry)
    {
        const string servicePrefix = "service-starttype:";
        const string taskPrefix = "scheduled-task:";

        if (entry.SettingId.StartsWith(servicePrefix, StringComparison.Ordinal))
        {
            var name = entry.SettingId[servicePrefix.Length..];
            var change = ServiceChangeFactory.CreateStartTypeChange(
                new ServiceEntry
                {
                    ServiceName = name,
                    DisplayName = name,
                    State = ServiceState.Running,
                    StartType = ServiceStartType.Manual,
                },
                ServiceStartType.Disabled);
            return change.SettingId == entry.SettingId ? change.AfterValue : null;
        }

        if (entry.SettingId.StartsWith(taskPrefix, StringComparison.Ordinal))
        {
            var path = entry.SettingId[taskPrefix.Length..];
            var change = ScheduledTaskChangeFactory.CreateToggle(
                new ScheduledTaskEntry
                {
                    Name = path[(path.LastIndexOf('\\') + 1)..],
                    Path = path,
                    IsEnabled = true,
                    Classification = TaskClassification.Unknown,
                },
                enable: false);
            return change.SettingId == entry.SettingId ? change.AfterValue : null;
        }

        return null;
    }

    [Fact]
    public void BundledSets_LoadCleanly()
    {
        Assert.Empty(_result.Warnings);
        Assert.Equal(
            ["Clean Boot", "NukeCopilot", "Privacy Baseline", "Windows 10-ify", "Windows Update Control"],
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
                string? expectedValue;
                if (entry.ModuleId == "Startup & Services")
                {
                    expectedValue = StartupFactoryDesiredValue(entry);
                    Assert.True(
                        expectedValue is not null,
                        $"{set.Name}: settingId doesn't match a Startup factory format ({entry.SettingId})");
                }
                else
                {
                    Assert.True(
                        desired.TryGetValue((entry.ModuleId, entry.SettingId), out expectedValue),
                        $"{set.Name}: unknown setting ({entry.ModuleId}, {entry.SettingId})");
                }
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

    /// <summary>
    /// The tweak inventory's opt-in-only services (functional breakage: search,
    /// notifications, sharing, printing, biometrics) must never become default Clean
    /// Boot entries. WbioSrvc especially: disabling it without the Biometrics policy
    /// companion hangs security dialogs on machines with a fingerprint sensor.
    /// </summary>
    [Fact]
    public void CleanBoot_NeverIncludesTheOptInRiskServices()
    {
        var cleanBoot = _result.Sets.Single(s => s.Name == "Clean Boot");

        string[] optInOnly = ["WSearch", "WbioSrvc", "CDPSvc", "WpnService", "Spooler"];
        foreach (var service in optInOnly)
        {
            Assert.DoesNotContain(
                ServiceChangeFactory.GetSettingId(service),
                cleanBoot.Entries.Select(e => e.SettingId));
        }

        Assert.All(cleanBoot.Entries, e => Assert.Equal("Startup & Services", e.ModuleId));
    }
}
