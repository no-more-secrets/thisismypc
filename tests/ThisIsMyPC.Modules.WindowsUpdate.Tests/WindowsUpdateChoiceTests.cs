using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.WindowsUpdate.Services;
using ThisIsMyPC.Modules.WindowsUpdate.Tests.Fakes;

namespace ThisIsMyPC.Modules.WindowsUpdate.Tests;

public sealed class WindowsUpdateChoiceTests
{
    [Theory]
    [InlineData("auto-update-mode", "4|2|20")]
    [InlineData("active-hours-manual", "2|20|7")]
    public void PresetsKeepAllFieldsAndInspectTheirValues(string id, string encoded)
    {
        var registry = new FakeRegistryService();
        var inspector = new WindowsUpdateSetEntryInspector(registry);
        var entry = new Core.Sets.SetEntry { ModuleId = "Windows Update", SettingId = id, Value = encoded, Description = "Test" };
        var group = inspector.CreateChangeGroup(entry)!;
        var values = group.Changes.Select(change => new Core.Sets.SetValue(change.SystemLocation, change.ValueType, change.AfterValue)).ToArray();
        Assert.Equal(encoded, new WindowsUpdateSetValueEncoder().Encode(id, values));
        Assert.False(inspector.Inspect(entry)!.IsApplied);
        foreach (var change in group.Changes)
        {
            var split = change.SystemLocation.LastIndexOf('\\');
            registry.SetDWord(change.SystemLocation[..split], change.SystemLocation[(split + 1)..], int.Parse(change.AfterValue!, System.Globalization.CultureInfo.InvariantCulture));
        }
        Assert.True(inspector.Inspect(entry)!.IsApplied);
        Assert.False(inspector.Inspect(entry with { Value = id == "auto-update-mode" ? "4|2|21" : "2|20|8" })!.IsApplied);
    }

    [Fact]
    public void MalformedActiveHoursBlockWithoutCrashingAndLegacyRemovalStillInspects()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.UxSettingsKeyPath, "ActiveHoursStart", "eight");
        var card = new WindowsUpdateChoiceSettings(registry).CreateCard("active-hours-manual");
        Assert.True(card.ReadPolicyState!().BlocksChanges);
        Assert.Empty(card.ChoiceFields);
        var entry = new Core.Sets.SetEntry { ModuleId = "Windows Update", SettingId = "active-hours-manual", Value = "", Description = "Default" };
        Assert.True(new WindowsUpdateSetEntryInspector(registry).Inspect(entry)!.IsApplied);
        registry.SetDWord(WindowsUpdateRegistryPaths.UxSettingsKeyPath, "SmartActiveHoursState", 2);
        Assert.False(new WindowsUpdateSetEntryInspector(registry).Inspect(entry with { Value = "2|20|7" })!.IsApplied);
        Assert.Null(new WindowsUpdateSetEntryInspector(registry).CreateChangeGroup(entry with { Value = "2|20|7" }));
    }

    [Fact]
    public void CoveredNotificationPresetCanStillBuildPreview()
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "NoAutoUpdate", 1);
        var inspector = new WindowsUpdateSetEntryInspector(registry);
        var entry = new Core.Sets.SetEntry { ModuleId = "Windows Update", SettingId = "auto-update-mode", Value = "2", Description = "Notify" };
        Assert.True(inspector.Inspect(entry)!.IsApplied);
        Assert.NotNull(inspector.CreateChangeGroup(entry));
        registry.SetString(WindowsUpdateRegistryPaths.CurrentVersionKeyPath, "EditionID", "Professional");
        var resolver = new Core.Sets.SetConflictResolver([inspector], _ => new Core.Modules.ModuleAvailability(true),
            new Core.Services.CapabilityDetector(registry), new Core.Policies.PolicyControlStateReader(registry));
        var definition = new Core.Sets.SetDefinition { Name = "Test", Description = "Test", Author = "Test", Version = "1",
            Category = Core.Sets.SetCategory.TweakSet, Source = Core.Sets.SetSource.User, FilePath = "test", Entries = [entry] };
        Assert.NotNull(Assert.Single(resolver.Resolve(definition, [])).State!.CoveredByPolicy);
        registry.SetDWord(WindowsUpdateRegistryPaths.UxSettingsKeyPath, "SmartActiveHoursState", 2);
        registry.SetString(WindowsUpdateRegistryPaths.UxSettingsKeyPath, "ActiveHoursStart", "invalid");
        var unreadable = definition with { Entries = [entry with { SettingId = "active-hours-manual", Value = "2|20|7" }] };
        Assert.NotNull(Assert.Single(resolver.Resolve(unreadable, [])).SkipReason);
    }

    [Fact]
    public async Task ScheduleCapturesLiveValuesAndUndoRestoresAbsence()
    {
        var registry = new FakeRegistryService();
        var choices = new WindowsUpdateChoiceSettings(registry);
        var card = choices.CreateCard("auto-update-mode");
        registry.SetDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions", 3);
        var group = card.CreateConfiguredChoiceGroup!("4", new Dictionary<string, string>
            { ["ScheduledInstallDay"] = "2", ["ScheduledInstallTime"] = "20" });
        Assert.Equal(["3", "", ""], group.Changes.Select(change => change.BeforeValue));
        Assert.Equal(["4", "2", "20"], group.Changes.Select(change => change.AfterValue));
        var module = new WindowsUpdateModule(registry);
        foreach (var change in group.Changes) Assert.True((await module.ApplyChangeAsync(change)).IsSuccess);
        foreach (var change in group.Changes.Reverse())
            Assert.True((await module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(3, registry.ReadDWord(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions").Value);
        Assert.False(registry.ValueExists(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "ScheduledInstallTime").Value);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("")]
    public void SharingRetainsAllSupportedModes(string mode)
    {
        var registry = new FakeRegistryService();
        registry.SetDWord(WindowsUpdateRegistryPaths.DeliveryOptimizationPoliciesKeyPath, "DODownloadMode", 2);
        var change = Assert.Single(new WindowsUpdateChoiceSettings(registry).Create("delivery-optimization", mode).Changes);
        Assert.Equal("2", change.BeforeValue);
        Assert.Equal(mode, change.AfterValue);
        Assert.Null(change.Enforcement!.GPCacheEntries);
    }

    [Fact]
    public void ManualHoursValidateOvernightRangesAndPreserveHoursWhenAutomatic()
    {
        var choices = new WindowsUpdateChoiceSettings(new FakeRegistryService());
        var values = new Dictionary<string, string> { ["ActiveHoursStart"] = "20", ["ActiveHoursEnd"] = "7" };
        Assert.Equal(["2", "20", "7"], choices.Create("active-hours-manual", "2", values).Changes.Select(change => change.AfterValue));
        values["ActiveHoursEnd"] = "20";
        Assert.Throws<ArgumentException>(() => choices.Create("active-hours-manual", "2", values));
        values["ActiveHoursEnd"] = "15";
        Assert.Throws<ArgumentException>(() => choices.Create("active-hours-manual", "2", values));
        Assert.Single(choices.Create("active-hours-manual", "1").Changes);
    }

    [Fact]
    public void UnknownTypesAndModesDoNotBecomeDefaults()
    {
        var registry = new FakeRegistryService();
        registry.SetString(WindowsUpdateRegistryPaths.AuPoliciesKeyPath, "AUOptions", "4");
        var choices = new WindowsUpdateChoiceSettings(registry);
        Assert.True(choices.CreateCard("auto-update-mode").ReadPolicyState!().BlocksChanges);
        Assert.Throws<InvalidOperationException>(() => choices.Create("auto-update-mode", "2"));
        Assert.Throws<ArgumentException>(() => choices.Create("delivery-optimization", "100"));
        registry.SetDWord(WindowsUpdateRegistryPaths.UxSettingsKeyPath, "SmartActiveHoursState", 99);
        var entry = new Core.Sets.SetEntry { ModuleId = "Windows Update", SettingId = "active-hours-manual", Value = "1", Description = "Automatic" };
        Assert.Null(new WindowsUpdateSetEntryInspector(registry).CreateChangeGroup(entry));
    }
}
