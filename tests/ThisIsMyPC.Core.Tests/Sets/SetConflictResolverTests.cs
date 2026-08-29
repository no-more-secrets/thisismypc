using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Core.Tests.Sets;

public sealed class SetConflictResolverTests
{
    private const string Module = "Stub Module";

    private sealed class StubInspector : ISetEntryInspector
    {
        public string ModuleId => Module;
        public Func<SetEntry, SetEntryState?> OnInspect { get; init; } = _ => null;
        public Func<SetEntry, ChangeGroup?> OnCreate { get; init; } = _ => null;

        public SetEntryState? Inspect(SetEntry entry) => OnInspect(entry);
        public ChangeGroup? CreateChangeGroup(SetEntry entry) => OnCreate(entry);
    }

    private static SetEntry Entry(string settingId = "s1", string value = "0") => new()
    {
        ModuleId = Module,
        SettingId = settingId,
        Value = value,
        Description = "d",
    };

    private static SetDefinition Definition(params SetEntry[] entries) => new()
    {
        Name = "Test",
        Description = "d",
        Category = SetCategory.TweakSet,
        Version = "1.0.0",
        Author = "t",
        Entries = entries,
        Source = SetSource.User,
        FilePath = "t.json",
    };

    private static SetEntryState State(bool isApplied) => new()
    {
        SettingDisplayName = "Setting",
        CurrentValue = "1",
        CurrentDisplay = "Windows default",
        IsApplied = isApplied,
    };

    private static ChangeGroup Group(string groupId, string settingId, string afterValue) => new()
    {
        GroupId = groupId,
        DisplayName = "g",
        Description = "g",
        Changes =
        [
            new ChangeDescriptor
            {
                ModuleId = Module,
                SettingId = settingId,
                DisplayName = "c",
                SystemLocation = @"HKCU\Test",
                BeforeValue = "1",
                AfterValue = afterValue,
                BeforeDisplay = "b",
                AfterDisplay = "a",
                ValueType = ChangeValueType.Registry_DWord,
            },
        ],
    };

    private static StubInspector StageableInspector(bool isApplied = false) => new()
    {
        OnInspect = _ => State(isApplied),
        OnCreate = e => Group("new", e.SettingId, e.Value),
    };

    [Fact]
    public void UnknownModule_Skipped()
    {
        var resolver = new SetConflictResolver([], _ => null);

        var resolution = resolver.Resolve(Definition(Entry()), []).Single();

        Assert.True(resolution.IsSkipped);
        Assert.Contains(Module, resolution.SkipReason, StringComparison.Ordinal);
        Assert.False(resolution.IncludedByDefault);
    }

    [Fact]
    public void UnavailableModule_SkippedWithReason()
    {
        var resolver = new SetConflictResolver(
            [], _ => new ModuleAvailability(IsAvailable: false, Reason: "needs elevation"));

        var resolution = resolver.Resolve(Definition(Entry()), []).Single();

        Assert.True(resolution.IsSkipped);
        Assert.Contains("needs elevation", resolution.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownSetting_Skipped()
    {
        var resolver = new SetConflictResolver(
            [new StubInspector()], _ => new ModuleAvailability(IsAvailable: true));

        var resolution = resolver.Resolve(Definition(Entry()), []).Single();

        Assert.True(resolution.IsSkipped);
        Assert.Contains("not recognized", resolution.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnstageableValue_Skipped_WithValueInReason()
    {
        var inspector = new StubInspector { OnInspect = _ => State(isApplied: false) };
        var resolver = new SetConflictResolver(
            [inspector], _ => new ModuleAvailability(IsAvailable: true));

        var resolution = resolver.Resolve(Definition(Entry(value: "bogus")), []).Single();

        Assert.True(resolution.IsSkipped);
        Assert.Contains("'bogus'", resolution.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanEntry_NoConflict_IncludedByDefault()
    {
        var resolver = new SetConflictResolver(
            [StageableInspector()], _ => new ModuleAvailability(IsAvailable: true));

        var resolution = resolver.Resolve(Definition(Entry()), []).Single();

        Assert.Equal(SetEntryConflict.None, resolution.Conflict);
        Assert.True(resolution.IncludedByDefault);
        Assert.NotNull(resolution.State);
    }

    [Fact]
    public void AppliedEntry_MarkedAlreadyApplied_ExcludedByDefault()
    {
        var resolver = new SetConflictResolver(
            [StageableInspector(isApplied: true)], _ => new ModuleAvailability(IsAvailable: true));

        var resolution = resolver.Resolve(Definition(Entry()), []).Single();

        Assert.Equal(SetEntryConflict.AlreadyApplied, resolution.Conflict);
        Assert.False(resolution.IncludedByDefault);
    }

    [Fact]
    public void PendingSameValue_MarkedAlreadyStaged_ExcludedByDefault()
    {
        var resolver = new SetConflictResolver(
            [StageableInspector()], _ => new ModuleAvailability(IsAvailable: true));

        var resolution = resolver
            .Resolve(Definition(Entry(value: "0")), [Group("p1", "s1", "0")])
            .Single();

        Assert.Equal(SetEntryConflict.PendingSameValue, resolution.Conflict);
        Assert.Equal("p1", resolution.PendingGroupId);
        Assert.False(resolution.IncludedByDefault);
    }

    [Fact]
    public void PendingDifferentValue_MarkedConflict_WithPendingDetails()
    {
        var resolver = new SetConflictResolver(
            [StageableInspector()], _ => new ModuleAvailability(IsAvailable: true));

        var resolution = resolver
            .Resolve(Definition(Entry(value: "0")), [Group("p1", "s1", "9")])
            .Single();

        Assert.Equal(SetEntryConflict.PendingDifferentValue, resolution.Conflict);
        Assert.Equal("p1", resolution.PendingGroupId);
        Assert.Equal("9", resolution.PendingValue);
        Assert.False(resolution.IncludedByDefault);
    }

    private sealed class StubCapabilityDetector : ICapabilityDetector
    {
        public WindowsSku? Sku { get; init; }
        public string? SkuDetectionFailureReason => null;
        public bool IsSkuRestricted(WindowsSku? restriction)
            => restriction is { } required && Sku is { } current && current.Tier() < required.Tier();
        public bool IsAvailable(SystemCapability capability) => true;
        public ModuleAvailability GetAvailability(SystemCapability capability) => new(true);
        public bool IsOwnerModeAvailable => false;
        public IReadOnlyList<CapabilityReportRow> GetCapabilityReport() => [];
    }

    private static StubInspector SkuRestrictedInspector(WindowsSku restrictedOn) => new()
    {
        OnInspect = _ => State(isApplied: false),
        OnCreate = e => Group("new", e.SettingId, e.Value) with
        {
            Changes =
            [
                Group("new", e.SettingId, e.Value).Changes[0] with
                {
                    Enforcement = new SettingEnforcement { SkuRestriction = restrictedOn },
                },
            ],
        },
    };

    [Fact]
    public void SkuBelowTheMinimumTier_ProducesCosmeticNotice_StillIncluded()
    {
        var resolver = new SetConflictResolver(
            [SkuRestrictedInspector(WindowsSku.Pro)],
            _ => new ModuleAvailability(IsAvailable: true),
            new StubCapabilityDetector { Sku = WindowsSku.Home });

        var resolution = resolver.Resolve(Definition(Entry()), []).Single();

        Assert.NotNull(resolution.SkuNotice);
        Assert.Contains("Home", resolution.SkuNotice, StringComparison.Ordinal);
        Assert.Contains("Cosmetic", resolution.SkuNotice, StringComparison.Ordinal);
        // Informational only: the entry stays stageable and included by default
        Assert.False(resolution.IsSkipped);
        Assert.True(resolution.IncludedByDefault);
    }

    [Fact]
    public void SkuAtOrAboveTheMinimumTier_NoNotice()
    {
        var resolver = new SetConflictResolver(
            [SkuRestrictedInspector(WindowsSku.Pro)],
            _ => new ModuleAvailability(IsAvailable: true),
            new StubCapabilityDetector { Sku = WindowsSku.Education });

        Assert.Null(resolver.Resolve(Definition(Entry()), []).Single().SkuNotice);
    }

    [Fact]
    public void UnknownSkuOrNoDetector_NoNotice()
    {
        var withUnknownSku = new SetConflictResolver(
            [SkuRestrictedInspector(WindowsSku.Pro)],
            _ => new ModuleAvailability(IsAvailable: true),
            new StubCapabilityDetector { Sku = null });
        Assert.Null(withUnknownSku.Resolve(Definition(Entry()), []).Single().SkuNotice);

        var withoutDetector = new SetConflictResolver(
            [SkuRestrictedInspector(WindowsSku.Pro)],
            _ => new ModuleAvailability(IsAvailable: true));
        Assert.Null(withoutDetector.Resolve(Definition(Entry()), []).Single().SkuNotice);
    }

    [Fact]
    public void PendingChangeForDifferentSetting_DoesNotConflict()
    {
        var resolver = new SetConflictResolver(
            [StageableInspector()], _ => new ModuleAvailability(IsAvailable: true));

        var resolution = resolver
            .Resolve(Definition(Entry(settingId: "s1")), [Group("p1", "other-setting", "0")])
            .Single();

        Assert.Equal(SetEntryConflict.None, resolution.Conflict);
        Assert.True(resolution.IncludedByDefault);
    }
}
