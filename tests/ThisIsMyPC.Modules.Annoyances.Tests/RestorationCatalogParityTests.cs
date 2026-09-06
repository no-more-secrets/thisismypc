using ThisIsMyPC.Core.Cards;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Annoyances.Changes;
using ThisIsMyPC.Modules.Annoyances.Models;
using ThisIsMyPC.Modules.Annoyances.Services;
using ThisIsMyPC.Modules.Annoyances.Tests.Fakes;

namespace ThisIsMyPC.Modules.Annoyances.Tests;

/// <summary>
/// Compile-checked parity between RestorationCatalog.Default and the changes the
/// Annoyances module actually emits (owner-mode-restoration.md, "Parity").
/// RestorationCatalogTests in Core.Tests pins the catalog against a hand-written
/// table; it cannot see the module. These tests never restate a catalog value.
/// Every expectation is the real AnnoyancesSettingsReader, AnnoyancesCardProvider,
/// and AnnoyanceChangeFactory output over the fake registry, projected into a
/// baseline by the real DriftBaselineStore. A provider or factory change that
/// moves a key, flips a value, adds a restart, adds enforcement the batch cannot
/// honor, or makes a new setting restorable fails here.
/// </summary>
public sealed class RestorationCatalogParityTests : IDisposable
{
    // The catalog batch covers only user-hive values, so every entry it accepts
    // carries the profile SID the baseline was written under.
    private const string UserSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    private readonly FakeRegistryService _registry = new();
    private readonly string _baselineDirectory =
        Path.Combine(Path.GetTempPath(), $"tipc-restore-parity-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_baselineDirectory))
                Directory.Delete(_baselineDirectory, recursive: true);
        }
        catch (IOException) { }
    }

    public static TheoryData<string> CatalogSettingIds()
    {
        var ids = new TheoryData<string>();
        foreach (var target in RestorationCatalog.Default.Targets)
            ids.Add(target.SettingId);
        return ids;
    }

    [Fact]
    public void Catalog_lists_every_restorable_single_the_module_emits()
    {
        // Structural rules from owner-mode-restoration.md: a background restore
        // writes one user-hive, non-policy, DWORD value with an explicit value in
        // both directions and no restart. These two ids meet all of that and are
        // deliberately outside the first batch; adding a qualifying setting to the
        // module means deciding whether it belongs in the catalog.
        string[] restorableButNotCataloged = ["auto-game-mode", "xbox-game-tips"];

        var emitted = Cards()
            .Where(IsRestorableSingle)
            .Select(card => card.Model.SettingId)
            .Order(StringComparer.Ordinal);
        var expected = RestorationCatalog.Default.Targets
            .Select(target => target.SettingId)
            .Concat(restorableButNotCataloged)
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, emitted);
    }

    [Theory]
    [MemberData(nameof(CatalogSettingIds))]
    public void Catalog_identity_matches_the_change_the_module_emits(string settingId)
    {
        var target = Target(settingId);
        var suppress = Emit(settingId, suppress: true);
        var restore = Emit(settingId, suppress: false);

        foreach (var change in new[] { suppress, restore })
        {
            Assert.Equal(change.ModuleId, target.ModuleId);
            Assert.Equal(change.SettingId, target.SettingId);
            Assert.Equal(change.DisplayName, target.DisplayName);
            Assert.Equal(change.ValueType, target.ValueType);
            Assert.Equal(change.SystemLocation, $@"{target.KeyPath}\{target.ValueName}");
        }
    }

    [Theory]
    [MemberData(nameof(CatalogSettingIds))]
    public void Catalog_allows_exactly_the_two_values_the_factory_writes(string settingId)
    {
        var target = Target(settingId);
        var suppress = Emit(settingId, suppress: true);
        var restore = Emit(settingId, suppress: false);

        Assert.Equal(suppress.AfterValue, target.SuppressedValue.Data);
        Assert.Equal(restore.AfterValue, target.WindowsDefaultValue.Data);
        Assert.Equal(
            [suppress.AfterValue, restore.AfterValue],
            target.AllowedDesiredValues.Select(value => value.Data));
        Assert.All(target.AllowedDesiredValues, value => Assert.Equal(RegistryValueDataKind.DWord, value.Kind));
    }

    [Theory]
    [MemberData(nameof(CatalogSettingIds))]
    public void Emitted_change_needs_no_restart(string settingId)
    {
        Assert.Equal(RestartRequirement.None, Emit(settingId, suppress: true).RestartRequirement);
        Assert.Equal(RestartRequirement.None, Emit(settingId, suppress: false).RestartRequirement);
    }

    [Theory]
    [MemberData(nameof(CatalogSettingIds))]
    public void Baseline_written_from_the_emitted_change_validates_as_a_candidate(string settingId)
    {
        var target = Target(settingId);

        foreach (var suppress in new[] { true, false })
        {
            var change = Emit(settingId, suppress);
            var entry = RecordBaseline(change);

            var result = RestorationCatalog.Default.Validate(entry, UserSid);

            Assert.Equal(RestorationValidationOutcome.Accepted, result.Outcome);
            Assert.Equal(change.AfterValue, result.Candidate!.DesiredValue.Data);
            Assert.Equal(RegistryValueDataKind.DWord, result.Candidate.DesiredValue.Kind);
            Assert.Equal(UserSid, result.Candidate.UserSid);
            Assert.Equal($@"HKU\{UserSid}\{target.KeyPath[@"HKCU\".Length..]}", result.Candidate.ResolvedKeyPath);
            Assert.Same(target, result.Candidate.Target);
        }
    }

    [Theory]
    [MemberData(nameof(CatalogSettingIds))]
    public void Emitted_enforcement_matches_the_provenance_line(string settingId)
    {
        var target = Target(settingId);
        var suppress = Emit(settingId, suppress: true);
        var restore = Emit(settingId, suppress: false);

        // Enforcement is attached on the suppress direction only (26-4 rule), so
        // the restore direction never carries metadata into the baseline.
        Assert.Null(restore.Enforcement);
        Assert.Null(RecordBaseline(restore).EnforcementJson);

        if (target.Provenance.Contains(nameof(AnnoyanceChangeFactory.CreateDriftFragileToggle), StringComparison.Ordinal))
        {
            // Reversion vectors are informational, the one enforcement shape a
            // background restore can honor.
            Assert.NotNull(suppress.Enforcement);
            var enforcement = suppress.Enforcement!;
            Assert.NotEmpty(enforcement.ReversionVectors!);
            Assert.Null(enforcement.CompanionServices);
            Assert.Null(enforcement.CompanionTasks);
            Assert.Null(enforcement.GPCacheEntries);
            Assert.Null(enforcement.SkuRestriction);
            Assert.False(enforcement.OwnerModeRequired);
            Assert.False(enforcement.AclElevation);
            Assert.False(enforcement.RestoresCompanions);
            Assert.NotNull(RecordBaseline(suppress).EnforcementJson);
        }
        else
        {
            Assert.Contains(nameof(AnnoyanceChangeFactory.CreateToggle), target.Provenance, StringComparison.Ordinal);
            Assert.Null(suppress.Enforcement);
            Assert.Null(RecordBaseline(suppress).EnforcementJson);
        }
    }

    [Theory]
    [MemberData(nameof(CatalogSettingIds))]
    public void Emitted_values_do_not_depend_on_live_registry_state(string settingId)
    {
        var target = Target(settingId);

        // The catalog's allowed list is closed, so the factory must write the same
        // pair whatever the machine currently holds; only BeforeValue may move.
        foreach (var live in target.AllowedDesiredValues)
        {
            _registry.SetDWord(target.KeyPath, target.ValueName, live.AsDWord());

            var suppress = Emit(settingId, suppress: true);
            var restore = Emit(settingId, suppress: false);

            Assert.Equal(target.SuppressedValue.Data, suppress.AfterValue);
            Assert.Equal(target.WindowsDefaultValue.Data, restore.AfterValue);
            Assert.Equal(live.Data, suppress.BeforeValue);
            Assert.Equal(live.Data, restore.BeforeValue);
        }
    }

    [Fact]
    public void Drifted_module_output_is_refused_by_the_catalog()
    {
        // Guards the parity checks themselves: a real provider or factory change
        // reaches the catalog as a changed descriptor, and the catalog must reject
        // it rather than restore something the module no longer writes.
        var change = Emit("advertising-id", suppress: true);

        var movedKey = RestorationCatalog.Default.Validate(
            RecordBaseline(change with { SystemLocation = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Moved\Enabled" }),
            UserSid);
        var newValue = RestorationCatalog.Default.Validate(
            RecordBaseline(change with { AfterValue = "2" }), UserSid);
        var newEnforcement = RestorationCatalog.Default.Validate(
            RecordBaseline(change with { Enforcement = new SettingEnforcement { OwnerModeRequired = true } }),
            UserSid);

        Assert.Equal(RestorationValidationOutcome.UnknownTarget, movedKey.Outcome);
        Assert.Equal(RestorationValidationOutcome.ValueNotAllowed, newValue.Outcome);
        Assert.Equal(RestorationValidationOutcome.IncompatibleEnforcement, newEnforcement.Outcome);
    }

    private static RestorationTarget Target(string settingId)
        => RestorationCatalog.Default.Targets.Single(t => t.SettingId == settingId);

    private IReadOnlyList<SettingCardSource> Cards()
    {
        var scan = new AnnoyancesModule(_registry).ScanSystemStateAsync().GetAwaiter().GetResult();
        var provider = new AnnoyancesCardProvider(new AnnoyancesSettingsReader(_registry));
        return provider.BuildCards((AnnoyancesScanData)scan.Value!);
    }

    /// <summary>The one descriptor the module stages for a card, or null for a group card.</summary>
    private static ChangeDescriptor? SingleChange(SettingCardSource card, bool suppress)
    {
        var group = card.CreateToggleGroup(suppress);
        return group.Changes.Count == 1 ? group.Changes[0] : null;
    }

    private ChangeDescriptor Emit(string settingId, bool suppress)
    {
        var card = Cards().Single(c => c.Model.SettingId == settingId);
        return SingleChange(card, suppress)
            ?? throw new InvalidOperationException($"{settingId} stages a group, not a single change.");
    }

    private static bool IsRestorableSingle(SettingCardSource card)
    {
        var suppress = SingleChange(card, suppress: true);
        var restore = SingleChange(card, suppress: false);
        if (suppress is null || restore is null)
            return false;

        return suppress.ValueType == ChangeValueType.Registry_DWord
            && suppress.SystemLocation.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase)
            && !suppress.SystemLocation.Contains(@"\Policies\", StringComparison.OrdinalIgnoreCase)
            && suppress.RestartRequirement == RestartRequirement.None
            && restore.RestartRequirement == RestartRequirement.None
            && !string.IsNullOrEmpty(suppress.AfterValue)
            && !string.IsNullOrEmpty(restore.AfterValue);
    }

    /// <summary>
    /// Projects an emitted change into a baseline entry the way production does.
    /// EnforcementJson is internal to Core, so the real store is the only way to
    /// get the enforcement text the service would read back.
    /// </summary>
    private DriftBaselineEntry RecordBaseline(ChangeDescriptor change)
    {
        var path = Path.Combine(_baselineDirectory, $"{Guid.NewGuid():N}.json");
        new DriftBaselineStore(path, UserSid).RecordApplied([change]);

        var document = DriftBaselineStore.Load(path)!;
        Assert.Equal(UserSid, document.UserSid);
        return Assert.Single(document.Entries!);
    }
}
