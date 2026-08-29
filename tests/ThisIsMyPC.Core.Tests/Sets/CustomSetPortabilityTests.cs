using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Core.Tests.Sets;

/// <summary>
/// Story 8.5 AC 5: a custom set written on one machine loads via ISetProvider on
/// another (file copied into its user sets directory) and normal conflict detection
/// reconciles the differing system state.
/// </summary>
public sealed class CustomSetPortabilityTests : IDisposable
{
    private const string Module = "Stub Module";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tipc-portability-{Guid.NewGuid():N}");
    private readonly string _sourceUserDir;
    private readonly string _targetUserDir;
    private readonly string _targetBuiltInDir;

    public CustomSetPortabilityTests()
    {
        _sourceUserDir = Path.Combine(_root, "source-sets");
        _targetUserDir = Path.Combine(_root, "target-sets");
        _targetBuiltInDir = Path.Combine(_root, "target-builtin");
        Directory.CreateDirectory(_targetUserDir);
        Directory.CreateDirectory(_targetBuiltInDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class StubInspector : ISetEntryInspector
    {
        public string ModuleId => Module;
        public required Func<SetEntry, bool> IsAppliedOnTarget { get; init; }

        public SetEntryState? Inspect(SetEntry entry) => new()
        {
            SettingDisplayName = entry.SettingId,
            CurrentValue = IsAppliedOnTarget(entry) ? entry.Value : "windows-default",
            CurrentDisplay = "current",
            IsApplied = IsAppliedOnTarget(entry),
        };

        public ChangeGroup? CreateChangeGroup(SetEntry entry) => new()
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = entry.SettingId,
            Description = entry.Description,
            Changes =
            [
                new ChangeDescriptor
                {
                    ModuleId = Module,
                    SettingId = entry.SettingId,
                    DisplayName = entry.SettingId,
                    SystemLocation = @"HKCU\Stub",
                    BeforeValue = "windows-default",
                    AfterValue = entry.Value,
                    BeforeDisplay = "before",
                    AfterDisplay = entry.DisplayValue,
                    ValueType = ChangeValueType.Registry_DWord,
                },
            ],
        };
    }

    private static ChangeGroup PendingGroup(string settingId, string afterValue) => new()
    {
        GroupId = Guid.NewGuid().ToString("N"),
        DisplayName = settingId,
        Description = $"Toggles {settingId}.",
        Changes =
        [
            new ChangeDescriptor
            {
                ModuleId = Module,
                SettingId = settingId,
                DisplayName = settingId,
                SystemLocation = @"HKCU\Stub",
                BeforeValue = "1",
                AfterValue = afterValue,
                BeforeDisplay = "Enabled",
                AfterDisplay = "Disabled",
                ValueType = ChangeValueType.Registry_DWord,
            },
        ],
    };

    [Fact]
    public void WrittenSet_CopiedToOtherMachine_LoadsAndResolvesAgainstDifferentState()
    {
        // "Source machine": save two staged toggles as a custom set.
        var writer = new CustomSetWriter(_sourceUserDir);
        var written = writer.WriteFromPendingGroups(
            new CustomSetMetadata
            {
                Name = "Travel Pack",
                Description = "My laptop tweaks.",
                Category = SetCategory.TweakSet,
            },
            [PendingGroup("advertising-id", "0"), PendingGroup("taskbar-widgets", "0")]);
        Assert.True(written.Success);

        // "File copy" to the target machine's user sets directory.
        File.Copy(written.FilePath!, Path.Combine(_targetUserDir, Path.GetFileName(written.FilePath!)));

        // Target machine: provider loads it as a user set.
        var loaded = new SetProvider(_targetBuiltInDir, _targetUserDir).LoadSets();
        Assert.Empty(loaded.Warnings);
        var set = Assert.Single(loaded.Sets);
        Assert.Equal(SetSource.User, set.Source);
        Assert.Equal(2, set.Entries.Count);

        // Target state differs from the source: advertising-id already matches the
        // set, taskbar-widgets does not.
        var resolver = new SetConflictResolver(
            [new StubInspector { IsAppliedOnTarget = e => e.SettingId == "advertising-id" }],
            _ => new ModuleAvailability(IsAvailable: true));

        var resolutions = resolver.Resolve(set, []);

        var applied = resolutions.Single(r => r.Entry.SettingId == "advertising-id");
        Assert.Equal(SetEntryConflict.AlreadyApplied, applied.Conflict);
        Assert.False(applied.IncludedByDefault);

        var stageable = resolutions.Single(r => r.Entry.SettingId == "taskbar-widgets");
        Assert.Equal(SetEntryConflict.None, stageable.Conflict);
        Assert.True(stageable.IncludedByDefault);
        Assert.False(stageable.IsSkipped);
    }
}
