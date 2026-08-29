using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Tests.Services;

public sealed class DisplayModePreferencesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-dm-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_dir, "display-modes.txt");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void MissingFile_ReturnsNullForAnyKey()
    {
        var store = new DisplayModePreferencesStore(FilePath);

        Assert.Null(store.Get("annoyances"));
    }

    [Fact]
    public void SetThenGet_RoundTrips_AcrossInstances()
    {
        new DisplayModePreferencesStore(FilePath).Set("annoyances", registryData: true, compact: false);

        var reloaded = new DisplayModePreferencesStore(FilePath).Get("annoyances");
        Assert.Equal((true, false), reloaded);
    }

    [Fact]
    public void MultipleTabs_PersistIndependently()
    {
        var store = new DisplayModePreferencesStore(FilePath);
        store.Set("annoyances", true, true);
        store.Set("explorer", false, true);

        var reloaded = new DisplayModePreferencesStore(FilePath);
        Assert.Equal((true, true), reloaded.Get("annoyances"));
        Assert.Equal((false, true), reloaded.Get("explorer"));
        Assert.Null(reloaded.Get("power"));
    }

    [Fact]
    public void CorruptLines_AreIgnored_ValidLinesStillLoad()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(FilePath,
        [
            "not a valid line",
            "=1|0",
            "annoyances=1|1",
            "broken=1",
            "broken2=1|0|extra",
        ]);

        var store = new DisplayModePreferencesStore(FilePath);
        Assert.Equal((true, true), store.Get("annoyances"));
        Assert.Null(store.Get("broken"));
        Assert.Null(store.Get("broken2"));
    }

    [Fact]
    public void Set_OverwritesExistingKey()
    {
        var store = new DisplayModePreferencesStore(FilePath);
        store.Set("annoyances", true, true);
        store.Set("annoyances", false, false);

        Assert.Equal((false, false), new DisplayModePreferencesStore(FilePath).Get("annoyances"));
    }
}
