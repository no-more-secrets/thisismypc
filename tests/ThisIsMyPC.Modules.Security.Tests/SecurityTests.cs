using ThisIsMyPC.App.UiTests.Fakes;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Sets;
using ThisIsMyPC.Core.Policies;

namespace ThisIsMyPC.Modules.Security.Tests;

public sealed class SecurityTests
{
    public static IEnumerable<object[]> Options() => SecurityCatalog.Settings.SelectMany(s => s.Choices.Select(o => new object[] { s.Id, o.Id }));

    [Theory]
    [MemberData(nameof(Options))]
    public async Task EveryOption_PreservesTypedBeforeStateAndCanUndo(string settingId, string optionId)
    {
        var registry = new UiFakeRegistryService();
        var reader = new SecuritySettings(registry);
        var module = new SecurityModule(registry);
        var setting = SecurityCatalog.Settings.Single(s => s.Id == settingId);
        var option = setting.Choices.Single(o => o.Id == optionId);
        // Start from a different explicit policy, not only absent values.
        var original = setting.Choices.First(o => o.Id != option.Id);
        foreach (var change in reader.Create(setting, original).Changes)
            Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        var group = reader.Create(setting, option);
        Assert.Equal(setting.Targets.Count, group.Changes.Count);
        foreach (var change in group.Changes)
        {
            Assert.True(SecurityCatalog.Allows(change));
            var result = await module.ApplyChangeAsync(change);
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }
        Assert.Equal(option.Id, reader.Read(setting).Option?.Id);
        foreach (var change in group.Changes.Reverse())
        {
            var result = await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue });
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }
        Assert.Equal(original.Id, reader.Read(setting).Option?.Id);
    }

    [Fact]
    public void CtrlAltDelete_UsesInverseValueAndDoesNotGuessWhenAbsent()
    {
        var registry = new UiFakeRegistryService();
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "secure-sign-in");
        var reader = new SecuritySettings(registry);
        Assert.Equal("default", reader.Read(setting).Option?.Id);
        registry.WriteDWord(setting.Targets[0].Key, "DisableCAD", 0);
        Assert.Equal("on", reader.Read(setting).Option?.Id);
        registry.WriteDWord(setting.Targets[0].Key, "DisableCAD", 1);
        Assert.Equal("off", reader.Read(setting).Option?.Id);
    }

    [Fact]
    public void UnknownAndWrongTypeValues_CannotProduceChanges()
    {
        var registry = new UiFakeRegistryService();
        var setting = SecurityCatalog.Settings[0];
        var reader = new SecuritySettings(registry);
        registry.WriteDWord(setting.Targets[0].Key, setting.Targets[0].Name, 99);
        Assert.Null(reader.Read(setting).Option);
        Assert.Throws<InvalidOperationException>(() => reader.Create(setting, setting.Choices[0]));
        registry.WriteString(setting.Targets[0].Key, setting.Targets[0].Name, "0");
        Assert.Null(reader.Read(setting).Option);
        Assert.Throws<InvalidOperationException>(() => reader.Create(setting, setting.Choices[0]));
    }

    [Fact]
    public async Task ChangedSinceStaging_IsNotOverwritten()
    {
        var registry = new UiFakeRegistryService();
        var setting = SecurityCatalog.Settings[0];
        var change = new SecuritySettings(registry).Create(setting, setting.Choices[1]).Changes[0];
        registry.WriteDWord(setting.Targets[0].Key, setting.Targets[0].Name, 1);
        Assert.False((await new SecurityModule(registry).ApplyChangeAsync(change)).IsSuccess);
        Assert.Equal(1, registry.ReadDWord(setting.Targets[0].Key, setting.Targets[0].Name).Value);
    }

    [Theory]
    [InlineData("Core", false, false)]
    [InlineData("Professional", true, false)]
    [InlineData("Enterprise", true, true)]
    [InlineData("Education", true, true)]
    public void Editions_GateTheQueue(string edition, bool signInAllowed, bool defenderAllowed)
    {
        var registry = RegistryFor(edition);
        var queue = new PendingChangesService(capabilityDetector: new CapabilityDetector(registry));
        foreach (var setting in SecurityCatalog.Settings)
        {
            var change = new SecuritySettings(registry).Create(setting, setting.Choices[1]);
            var allowed = setting.Edition == WindowsSku.Pro ? signInAllowed : defenderAllowed;
            if (allowed) queue.Stage(change);
            else Assert.Throws<InvalidOperationException>(() => queue.Stage(change));
        }
    }

    [Fact]
    public void BrokerCatalog_RejectsForgedTargetsValuesAndEnforcement()
    {
        var setting = SecurityCatalog.Settings[0];
        var change = new SecuritySettings(new UiFakeRegistryService()).Create(setting, setting.Choices[1]).Changes[0];
        Assert.False(SecurityCatalog.Allows(change with { SystemLocation = @"HKLM\Software\Other\Value" }));
        Assert.False(SecurityCatalog.Allows(change with { AfterValue = "999" }));
        Assert.False(SecurityCatalog.Allows(change with { BeforeValue = "999" }));
        Assert.False(SecurityCatalog.Allows(change with { Enforcement = new SettingEnforcement { SkuRestriction = WindowsSku.Home } }));
        Assert.False(SecurityCatalog.Allows(change with { Enforcement = change.Enforcement! with { AclElevation = true } }));
        Assert.False(SecurityCatalog.Allows(change with { Enforcement = change.Enforcement! with { CompanionServices = ["WinDefend"] } }));
    }

    [Fact]
    public void Presets_CompareCompanionValuesAndHandleAlreadyAppliedChoices()
    {
        var registry = RegistryFor("Enterprise");
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "smartscreen");
        var reader = new SecuritySettings(registry);
        var queued = reader.Create(setting, setting.Choices.Single(o => o.Id == "warn"));
        var resolver = new SetConflictResolver([new SecuritySetEntryInspector(registry)], _ => new ModuleAvailability(true), new CapabilityDetector(registry));
        SetDefinition Definition(string value) => new()
        {
            Name = "Security", Description = "Test", Author = "Test", Version = "1.0.0", Category = SetCategory.TweakSet,
            Source = SetSource.User, FilePath = "test.json",
            Entries = [new() { ModuleId = "Security", SettingId = setting.Id, Value = value, Description = "Test" }],
        };
        Assert.Equal(SetEntryConflict.PendingDifferentValue, resolver.Resolve(Definition("block"), [queued]).Single().Conflict);
        Assert.Equal(SetEntryConflict.PendingSameValue, resolver.Resolve(Definition("warn"), [queued]).Single().Conflict);
        Assert.Equal(SetEntryConflict.AlreadyApplied, resolver.Resolve(Definition("default"), []).Single().Conflict);
        Assert.Equal(SetEntryConflict.PendingDifferentValue, resolver.Resolve(Definition("default"), [queued]).Single().Conflict);
    }

    private static UiFakeRegistryService RegistryFor(string edition)
    {
        var registry = new UiFakeRegistryService();
        registry.WriteString(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", edition);
        return registry;
    }

    [Fact]
    public void SavedPolicy_KeepsKnownConfigurationVisibleButBlocksDirectWrites()
    {
        var registry = RegistryFor("Enterprise");
        var setting = SecurityCatalog.Settings.Single(s => s.Id == "real-time-protection");
        var target = setting.Targets[0];
        registry.WriteDWord(target.Key, target.Name, 1);
        var source = new PolicySourceSnapshot("Computer policy", PolicyScope.Machine, PolicyFileStatus.Loaded,
            [new(target.Key[5..], target.Name, 4, [1, 0, 0, 0])]);
        var policies = new PolicyControlStateReader(registry, () => [source]);
        var reader = new SecuritySettings(registry, policies);
        Assert.Equal("off", reader.Read(setting).Option?.Id);
        Assert.Contains("saved local policy", reader.Read(setting).BlockReason, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => reader.Create(setting, setting.Choices[0]));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void EveryOption_PassesSharedPolicyChecks(string settingId, string optionId)
    {
        var registry = RegistryFor("Enterprise");
        var setting = SecurityCatalog.Settings.Single(s => s.Id == settingId);
        var option = setting.Choices.Single(o => o.Id == optionId);
        foreach (var pair in setting.Targets.Select((target, index) => (target, index)))
        {
            var value = option.Values[pair.index];
            if (value == "") continue;
            if (pair.target.Type == ChangeValueType.Registry_String) registry.WriteString(pair.target.Key, pair.target.Name, value);
            else registry.WriteDWord(pair.target.Key, pair.target.Name, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        }
        var policies = new PolicyControlStateReader(registry);
        var reader = new SecuritySettings(registry, policies);
        Assert.Null(reader.Read(setting).BlockReason);
        var queue = new PendingChangesService(capabilityDetector: new CapabilityDetector(registry), policyStates: policies);
        queue.Stage(reader.Create(setting, setting.Choices[0]));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void PresetExport_RoundTripsPendingAndHistory(string settingId, string optionId)
    {
        var root = Path.Combine(Path.GetTempPath(), "tipc-security-" + Guid.NewGuid().ToString("N"));
        try
        {
            var registry = new UiFakeRegistryService();
            var setting = SecurityCatalog.Settings.Single(s => s.Id == settingId);
            var option = setting.Choices.Single(o => o.Id == optionId);
            var group = new SecuritySettings(registry).Create(setting, option);
            var writer = new CustomSetWriter(root, [new SecuritySetValueEncoder()]);
            var metadata = new CustomSetMetadata { Name = "Security", Description = "Test", Category = SetCategory.TweakSet };
            Assert.True(writer.WriteFromPendingGroups(metadata, [group]).Success);
            var history = group.Changes.Select((c, index) => new ChangeHistoryEntry
            {
                Id = index + 1, GroupId = group.GroupId, ModuleId = c.ModuleId, SettingId = c.SettingId,
                DisplayName = c.DisplayName, SystemLocation = c.SystemLocation, ValueType = c.ValueType,
                BeforeValue = c.BeforeValue, AfterValue = c.AfterValue, BeforeDisplay = c.BeforeDisplay,
                AfterDisplay = c.AfterDisplay, AppliedAt = DateTimeOffset.UtcNow,
            }).Reverse().ToList();
            Assert.True(writer.WriteFromHistory(metadata, history).Success);
            var sets = new SetProvider(Path.Combine(root, "missing"), root).LoadSets().Sets;
            Assert.Equal(2, sets.Count);
            foreach (var set in sets)
            {
                var entry = Assert.Single(set.Entries);
                Assert.Equal(optionId, entry.Value);
                var restored = new SecuritySetEntryInspector(registry).CreateChangeGroup(entry);
                Assert.NotNull(restored);
                Assert.Equal(group.Changes.Select(c => c.AfterValue), restored.Changes.Select(c => c.AfterValue));
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HistoryExport_SplitsSecurityChoicesWithinOneApplyBatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "tipc-security-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = new Core.Data.ChangeHistoryRepository();
            var history = new ChangeHistoryService(repository, Path.Combine(root, "history.db"));
            await history.InitializeAsync();
            var reader = new SecuritySettings(new UiFakeRegistryService());
            var applied = new List<ChangeDescriptor>();
            foreach (var id in new[] { "secure-sign-in", "smartscreen" })
            {
                var setting = SecurityCatalog.Settings.Single(s => s.Id == id);
                applied.AddRange(reader.Create(setting, setting.Choices.Last()).Changes);
            }
            applied.Add(applied[0] with { ModuleId = "Other", SettingId = "other", AfterValue = "1" });
            await history.RecordChangesAsync(new() { IsSuccess = true, Applied = applied, RolledBack = [] });
            var writer = new CustomSetWriter(Path.Combine(root, "sets"), [new SecuritySetValueEncoder()]);
            var result = writer.WriteFromHistory(new() { Name = "Mixed", Description = "Test", Category = SetCategory.TweakSet }, await history.GetHistoryAsync());
            Assert.True(result.Success, result.Error);
            Assert.Equal(3, result.EntryCount);
            Assert.Equal(0, result.SkippedGroupCount);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
