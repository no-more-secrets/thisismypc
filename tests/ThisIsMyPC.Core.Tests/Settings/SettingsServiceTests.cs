using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Core.Tests.Settings;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"tipc-settings-{Guid.NewGuid():N}");
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private SettingsService Create() => new(_path);

    [Fact]
    public void Initialize_MissingFile_CreatesDefaults()
    {
        var service = Create();
        service.Initialize();

        Assert.False(service.SettingsWereReset);
        Assert.Equal("dark", service.GetApp(AppSettingKeys.Theme, "?"));
        Assert.True(service.GetAppBool(AppSettingKeys.UpdateCheck, fallback: false));
        Assert.True(File.Exists(_path)); // defaults written immediately
    }

    [Fact]
    public void SetApp_PersistsImmediately_AndRoundTrips()
    {
        var first = Create();
        first.Initialize();
        first.SetApp(AppSettingKeys.Theme, "light");

        var second = Create();
        second.Initialize();

        Assert.Equal("light", second.GetApp(AppSettingKeys.Theme, "?"));
    }

    [Fact]
    public void ModuleSettings_RoundTrip_AndNullWhenUnset()
    {
        var first = Create();
        first.Initialize();
        Assert.Null(first.GetModule("Windows Update", "someKey"));

        first.SetModule("Windows Update", "someKey", "42");

        var second = Create();
        second.Initialize();
        Assert.Equal("42", second.GetModule("Windows Update", "someKey"));
    }

    [Fact]
    public void SettingsFile_IsHumanReadableIndentedJson()
    {
        var service = Create();
        service.Initialize();
        service.SetApp(AppSettingKeys.Theme, "light");

        var text = File.ReadAllText(_path);

        Assert.Contains("\"appSettings\"", text, StringComparison.Ordinal);
        Assert.Contains("\"theme\": \"light\"", text, StringComparison.Ordinal);
        Assert.Contains('\n', text); // indented, not minified
    }

    [Fact]
    public void CorruptFile_ResetsToDefaults_FlagsReset_PreservesBadFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ this is not json");

        var service = Create();
        service.Initialize();

        Assert.True(service.SettingsWereReset);
        Assert.NotNull(service.LoadError);
        Assert.Equal("dark", service.GetApp(AppSettingKeys.Theme, "?"));
        Assert.True(File.Exists(_path + ".bad"));
        // The live file was rewritten with valid defaults
        var second = Create();
        second.Initialize();
        Assert.False(second.SettingsWereReset);
    }

    [Fact]
    public void SettingChanged_RaisedWithScopeKeyValue()
    {
        var service = Create();
        service.Initialize();
        SettingChangedEventArgs? received = null;
        service.SettingChanged += (_, e) => received = e;

        service.SetApp(AppSettingKeys.TrayMode, "1");

        Assert.NotNull(received);
        Assert.Equal(SettingChangedEventArgs.AppScope, received!.Scope);
        Assert.Equal(AppSettingKeys.TrayMode, received.Key);
        Assert.Equal("1", received.Value);

        service.SetModule("Explorer", "k", "v");
        Assert.Equal("Explorer", received!.Scope);
    }

    [Fact]
    public void UnknownTopLevelProperties_SurviveASave()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """
            {
              "appSettings": { "theme": "light" },
              "futureFeature": { "someKey": true }
            }
            """);

        var service = Create();
        service.Initialize();
        service.SetApp(AppSettingKeys.Theme, "dark"); // triggers a save

        var text = File.ReadAllText(_path);
        Assert.Contains("futureFeature", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableFile_DegradesToDefaults_AndNeverRewritesTheFile()
    {
        // An exclusively-locked file makes ReadAllText throw IOException — the
        // unreadable path, NOT the corrupt path.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """{ "appSettings": { "theme": "light" } }""");
        using (File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var service = Create();
            service.Initialize();

            Assert.False(service.SettingsWereReset);
            Assert.NotNull(service.LoadError);
            Assert.Equal("dark", service.GetApp(AppSettingKeys.Theme, "?"));

            // The safety-critical contract: saves stay disabled so the user's real
            // file is never clobbered from defaults.
            service.SetApp(AppSettingKeys.Theme, "light");
            Assert.Equal("light", service.GetApp(AppSettingKeys.Theme, "?")); // in-memory only
        }

        // The original content survived untouched
        Assert.Contains("\"theme\": \"light\"", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public void NullValuesInParseableJson_AreSkipped_NotACrash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """
            {
              "appSettings": { "theme": null, "closeAction": "tray" },
              "moduleSettings": { "Ghost": null, "Real": { "k": null, "ok": "1" } }
            }
            """);

        var service = Create();
        service.Initialize();

        Assert.Equal("dark", service.GetApp(AppSettingKeys.Theme, "?")); // null skipped → default
        Assert.Equal("tray", service.GetApp(AppSettingKeys.CloseAction, "?"));
        Assert.Null(service.GetModule("Ghost", "anything"));
        Assert.Null(service.GetModule("Real", "k"));
        Assert.Equal("1", service.GetModule("Real", "ok"));
    }

    [Fact]
    public void NewDefaults_BackfillIntoOlderFiles()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, """{ "appSettings": { "theme": "light" } }""");

        var service = Create();
        service.Initialize();

        // Keys absent from an older file fall back to current defaults
        Assert.Equal("light", service.GetApp(AppSettingKeys.Theme, "?"));
        Assert.Equal("exit", service.GetApp(AppSettingKeys.CloseAction, "?"));
    }
}
