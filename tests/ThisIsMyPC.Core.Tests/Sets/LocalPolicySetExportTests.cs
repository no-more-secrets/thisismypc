using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Core.Tests.Sets;

public sealed class LocalPolicySetExportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolicyAndPreference_ExportAsSeparateToggleValues(bool history)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tipc-local-policy-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var policy = Change("consumer-features", ChangeValueType.LocalPolicy_DWord, new LocalPolicyValue("0", "0").Encode());
            var preference = Change("windows-tips", ChangeValueType.Registry_DWord, "1");
            var companion = Change("windows-tips", ChangeValueType.LocalPolicy_DWord, new LocalPolicyValue("0", "0").Encode());
            var writer = new CustomSetWriter(directory);
            var metadata = new CustomSetMetadata { Name = "Policy toggles", Description = "Test", Category = SetCategory.TweakSet };
            var result = history
                ? writer.WriteFromHistory(metadata, new[] { policy, preference, companion }.Select((c, i) => new ChangeHistoryEntry
                {
                    Id = i + 1, GroupId = "batch", ModuleId = c.ModuleId, SettingId = c.SettingId, DisplayName = c.DisplayName,
                    SystemLocation = c.SystemLocation, ValueType = c.ValueType, BeforeValue = c.BeforeValue,
                    AfterValue = c.AfterValue, BeforeDisplay = c.BeforeDisplay, AfterDisplay = c.AfterDisplay, AppliedAt = DateTimeOffset.UtcNow,
                }).Reverse().ToList())
                : writer.WriteFromPendingGroups(metadata, [new() { GroupId = "1", DisplayName = "Consumer features", Description = "Test", Changes = [policy] },
                    new() { GroupId = "2", DisplayName = "Tips", Description = "Test", Changes = [preference, companion] }]);
            Assert.True(result.Success, result.Error);
            var set = Assert.Single(new SetProvider(Path.Combine(directory, "no-builtin"), directory).LoadSets().Sets);
            Assert.Equal(2, set.Entries.Count);
            Assert.Equal("0", set.Entries.Single(e => e.SettingId == "consumer-features").Value);
            Assert.Equal("1", set.Entries.Single(e => e.SettingId == "windows-tips").Value);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static ChangeDescriptor Change(string id, ChangeValueType type, string value) => new()
    {
        ModuleId = "Windows Annoyances", SettingId = id, DisplayName = id,
        SystemLocation = LocalPolicyToggleCatalog.Location("Windows Annoyances", id)!,
        ValueType = type, BeforeValue = "1", AfterValue = value, BeforeDisplay = "Suppressed", AfterDisplay = "Allowed",
    };
}
