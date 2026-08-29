using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Core.Tests.Sets;

public sealed class CustomSetWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tipc-setwriter-{Guid.NewGuid():N}");
    private readonly string _user;

    public CustomSetWriterTests()
    {
        _user = Path.Combine(_root, "sets");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private CustomSetWriter CreateSut() => new(_user);

    private static CustomSetMetadata Metadata(
        string name = "My Debloat", string description = "My favorite tweaks.",
        SetCategory category = SetCategory.TweakSet)
        => new() { Name = name, Description = description, Category = category };

    private static ChangeDescriptor Descriptor(
        string moduleId = "Windows Annoyances", string settingId = "advertising-id",
        string? afterValue = "0", string? afterDisplay = "Disabled")
        => new()
        {
            ModuleId = moduleId,
            SettingId = settingId,
            DisplayName = "Advertising ID",
            SystemLocation = @"HKCU\...\AdvertisingInfo",
            BeforeValue = "1",
            AfterValue = afterValue,
            BeforeDisplay = "Enabled",
            AfterDisplay = afterDisplay,
            ValueType = ChangeValueType.Registry_DWord,
        };

    private static ChangeGroup Group(
        string description = "Stops advertising ID tracking.", params ChangeDescriptor[] changes)
        => new()
        {
            GroupId = Guid.NewGuid().ToString("N"),
            DisplayName = "Advertising ID",
            Description = description,
            Changes = changes.Length == 0 ? [Descriptor()] : changes,
        };

    private static ChangeHistoryEntry HistoryEntry(
        long id, string? groupId, string settingId = "advertising-id",
        string? afterValue = "0", string? afterDisplay = "Disabled")
        => new()
        {
            Id = id,
            ModuleId = "Windows Annoyances",
            SettingId = settingId,
            DisplayName = "Advertising ID",
            SystemLocation = @"HKCU\...\AdvertisingInfo",
            BeforeValue = "1",
            AfterValue = afterValue,
            BeforeDisplay = "Enabled",
            AfterDisplay = afterDisplay,
            ValueType = ChangeValueType.Registry_DWord,
            GroupId = groupId,
            AppliedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void PendingGroups_WritesLoadableUserSet_WithMappedFields()
    {
        var result = CreateSut().WriteFromPendingGroups(Metadata(), [Group()]);

        Assert.True(result.Success);
        Assert.Equal(1, result.EntryCount);
        Assert.Equal(0, result.SkippedGroupCount);
        Assert.Equal(Path.Combine(_user, "my-debloat.json"), result.FilePath);

        // Round-trip: the provider must load the writer's output as a user set.
        var loaded = new SetProvider(Path.Combine(_root, "no-builtin"), _user).LoadSets();
        var set = Assert.Single(loaded.Sets);
        Assert.Equal("My Debloat", set.Name);
        Assert.Equal("My favorite tweaks.", set.Description);
        Assert.Equal(SetCategory.TweakSet, set.Category);
        Assert.Equal("1.0", set.Version);
        Assert.Equal(SetSource.User, set.Source);

        var entry = Assert.Single(set.Entries);
        Assert.Equal("Windows Annoyances", entry.ModuleId);
        Assert.Equal("advertising-id", entry.SettingId);
        Assert.Equal("0", entry.Value);
        Assert.Equal("Disabled", entry.DisplayValue);
        Assert.Equal("Stops advertising ID tracking.", entry.Description);
        Assert.Null(entry.Enforcement);
    }

    [Fact]
    public void MultiDescriptorGroup_CollapsesToOneEntry_UsingFirstDescriptor()
    {
        // A group toggle (e.g. copilot across two registry scopes) is one logical
        // setting — the schema stores the group's first value only.
        var group = Group("Removes Copilot.",
            Descriptor(settingId: "copilot", afterValue: "1", afterDisplay: "Suppressed"),
            Descriptor(settingId: "copilot", afterValue: "1", afterDisplay: "Suppressed"));

        var result = CreateSut().WriteFromPendingGroups(Metadata(), [group]);

        Assert.True(result.Success);
        Assert.Equal(1, result.EntryCount);
        var set = Assert.Single(new SetProvider(Path.Combine(_root, "nb"), _user).LoadSets().Sets);
        var entry = Assert.Single(set.Entries);
        Assert.Equal("copilot", entry.SettingId);
        Assert.Equal("1", entry.Value);
    }

    [Fact]
    public void GroupWithNullAfterValue_IsSkipped_AndCounted()
    {
        var deletion = Group("Deletes something.", Descriptor(afterValue: null, afterDisplay: null));

        var result = CreateSut().WriteFromPendingGroups(Metadata(), [Group(), deletion]);

        Assert.True(result.Success);
        Assert.Equal(1, result.EntryCount);
        Assert.Equal(1, result.SkippedGroupCount);
    }

    [Fact]
    public void EmptyGroupDescription_FallsBackToDisplayName()
    {
        var result = CreateSut().WriteFromPendingGroups(Metadata(), [Group(description: "")]);

        Assert.True(result.Success);
        var set = Assert.Single(new SetProvider(Path.Combine(_root, "nb"), _user).LoadSets().Sets);
        Assert.Equal("Advertising ID", Assert.Single(set.Entries).Description);
    }

    [Fact]
    public void NoUsableChanges_ReturnsError_AndWritesNothing()
    {
        var result = CreateSut().WriteFromPendingGroups(Metadata(), []);

        Assert.False(result.Success);
        Assert.Equal("No changes to save as a set.", result.Error);
        Assert.False(Directory.Exists(_user) && Directory.GetFiles(_user).Length > 0);
    }

    [Theory]
    [InlineData("", "desc", "Set name is required.")]
    [InlineData("   ", "desc", "Set name is required.")]
    [InlineData("name", "", "Set description is required.")]
    public void MissingMetadata_ReturnsError(string name, string description, string expectedError)
    {
        var result = CreateSut().WriteFromPendingGroups(
            Metadata(name: name, description: description), [Group()]);

        Assert.False(result.Success);
        Assert.Equal(expectedError, result.Error);
    }

    [Fact]
    public void FileNameCollision_AppendsSuffix_NeverOverwrites()
    {
        var sut = CreateSut();
        var first = sut.WriteFromPendingGroups(Metadata(), [Group()]);
        var second = sut.WriteFromPendingGroups(Metadata(), [Group()]);
        var third = sut.WriteFromPendingGroups(Metadata(), [Group()]);

        Assert.Equal(Path.Combine(_user, "my-debloat.json"), first.FilePath);
        Assert.Equal(Path.Combine(_user, "my-debloat-2.json"), second.FilePath);
        Assert.Equal(Path.Combine(_user, "my-debloat-3.json"), third.FilePath);
        Assert.Equal(3, Directory.GetFiles(_user).Length);
    }

    [Theory]
    [InlineData("My Debloat!!", "my-debloat.json")]
    [InlineData("  Édition Spéciale  ", "dition-sp-ciale.json")]
    [InlineData("***", "custom-set.json")]
    public void SetName_SlugifiesToSafeFileName(string name, string expectedFileName)
    {
        var result = CreateSut().WriteFromPendingGroups(Metadata(name: name), [Group()]);

        Assert.True(result.Success);
        Assert.Equal(expectedFileName, Path.GetFileName(result.FilePath));
    }

    [Fact]
    public void History_BatchesByGroupId_SoloRowsStandAlone()
    {
        ChangeHistoryEntry[] rows =
        [
            HistoryEntry(1, "batch-a", settingId: "copilot", afterValue: "1"),
            HistoryEntry(2, "batch-a", settingId: "copilot", afterValue: "1"),
            HistoryEntry(3, null, settingId: "taskbar-widgets", afterValue: "0", afterDisplay: "Hidden"),
        ];

        var result = CreateSut().WriteFromHistory(Metadata(), rows);

        Assert.True(result.Success);
        Assert.Equal(2, result.EntryCount);
        var set = Assert.Single(new SetProvider(Path.Combine(_root, "nb"), _user).LoadSets().Sets);
        Assert.Equal("copilot", set.Entries[0].SettingId);
        Assert.Equal("taskbar-widgets", set.Entries[1].SettingId);
        Assert.Equal("Advertising ID", set.Entries[0].Description); // DisplayName fallback
    }

    [Fact]
    public void History_NullAfterValueBatch_SkippedAndCounted()
    {
        ChangeHistoryEntry[] rows =
        [
            HistoryEntry(1, "batch-a"),
            HistoryEntry(2, "batch-b", afterValue: null, afterDisplay: null),
        ];

        var result = CreateSut().WriteFromHistory(Metadata(), rows);

        Assert.True(result.Success);
        Assert.Equal(1, result.EntryCount);
        Assert.Equal(1, result.SkippedGroupCount);
    }
}
