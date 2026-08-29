using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Drift;

namespace ThisIsMyPC.Core.Tests.Drift;

public sealed class DriftBaselineStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-drift-{Guid.NewGuid():N}");
    private readonly string _path;
    private readonly DriftBaselineStore _store;

    public DriftBaselineStoreTests()
    {
        _path = Path.Combine(_dir, "drift-baseline.json");
        _store = new DriftBaselineStore(_path);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
    }

    private static ChangeDescriptor Descriptor(
        string location = @"HKCU\Software\Test\Value",
        string after = "1",
        ChangeValueType valueType = ChangeValueType.Registry_DWord) => new()
    {
        ModuleId = "Windows Annoyances",
        SettingId = "copilot",
        DisplayName = "Copilot button",
        SystemLocation = location,
        BeforeValue = "0",
        AfterValue = after,
        BeforeDisplay = "On",
        AfterDisplay = "Off",
        ValueType = valueType,
        Category = ChangeCategory.Disable,
    };

    [Fact]
    public void RecordApplied_persists_expected_value_and_round_trips()
    {
        _store.RecordApplied([Descriptor()]);

        var loaded = DriftBaselineStore.Load(_path);
        var entry = Assert.Single(loaded!.Entries!);
        Assert.Equal(@"HKCU\Software\Test\Value", entry.SystemLocation);
        Assert.Equal("1", entry.ExpectedValue);
        Assert.Equal(ChangeValueType.Registry_DWord, entry.ValueType);
        Assert.Equal("copilot", entry.SettingId);
    }

    [Fact]
    public void RecordApplied_newest_write_to_a_location_wins()
    {
        _store.RecordApplied([Descriptor(after: "1")]);
        _store.RecordApplied([Descriptor(after: "0")]);

        var entry = Assert.Single(DriftBaselineStore.Load(_path)!.Entries!);
        Assert.Equal("0", entry.ExpectedValue);
    }

    [Fact]
    public void RecordApplied_skips_untrackable_value_types()
    {
        _store.RecordApplied([
            Descriptor(location: @"HKLM\SYSTEM\Svc", valueType: ChangeValueType.Service_StartType),
            Descriptor(location: @"Task\Path", valueType: ChangeValueType.ScheduledTask_State),
            Descriptor(),
        ]);

        var entry = Assert.Single(DriftBaselineStore.Load(_path)!.Entries!);
        Assert.Equal(@"HKCU\Software\Test\Value", entry.SystemLocation);
    }

    [Fact]
    public void RecordApplied_with_no_trackable_changes_writes_nothing()
    {
        _store.RecordApplied([Descriptor(valueType: ChangeValueType.PowerPlan_Setting)]);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Load_returns_null_for_missing_or_corrupt_file()
    {
        Assert.Null(DriftBaselineStore.Load(_path));

        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "not json {");
        Assert.Null(DriftBaselineStore.Load(_path));
    }
}
