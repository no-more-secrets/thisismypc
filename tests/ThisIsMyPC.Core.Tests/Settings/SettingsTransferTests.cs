using ThisIsMyPC.Core.Settings;

namespace ThisIsMyPC.Core.Tests.Settings;

public class SettingsTransferTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tipc-transfer-{Guid.NewGuid():N}");

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

    private SettingsService CreateService(string name)
    {
        var service = new SettingsService(Path.Combine(_dir, name));
        service.Initialize();
        return service;
    }

    [Fact]
    public void Export_ContainsMetadataAndAllScopes()
    {
        var source = CreateService("a.json");
        source.SetApp(AppSettingKeys.Theme, "light");
        source.SetModule("Windows Update", "k", "v");

        var json = SettingsTransfer.BuildExportJson(source, "1.2.3", "TEST-PC");
        var document = SettingsTransfer.Parse(json);

        Assert.NotNull(document);
        Assert.Equal("1.2.3", document!.AppVersion);
        Assert.Equal("TEST-PC", document.MachineName);
        Assert.NotNull(document.ExportedAt);
        Assert.Equal("light", document.AppSettings!["theme"]);
        Assert.Equal("v", document.ModuleSettings!["Windows Update"]["k"]);
    }

    [Fact]
    public void DefaultExportFileName_MatchesTheSpecFormat()
    {
        var name = SettingsTransfer.DefaultExportFileName(new DateTimeOffset(2026, 8, 29, 1, 2, 3, TimeSpan.Zero));
        Assert.Equal("thisismypc-settings-2026-08-29.json", name);
    }

    [Fact]
    public void Parse_RejectsGarbageAndForeignJson()
    {
        Assert.Null(SettingsTransfer.Parse("{ nope"));
        Assert.Null(SettingsTransfer.Parse("""{ "unrelated": true }"""));
    }

    [Fact]
    public void Preview_SkipsUnknownModules_AppliesKnownAndAppScope()
    {
        var source = CreateService("src.json");
        source.SetApp(AppSettingKeys.Theme, "light");
        source.SetModule("Windows Update", "k", "v");
        source.SetModule("ASUS Platform Tuning", "rgb", "on");
        var json = SettingsTransfer.BuildExportJson(source, "1.0.0", "SRC");

        var target = CreateService("dst.json");
        var preview = SettingsTransfer.BuildPreview(
            target, SettingsTransfer.Parse(json)!, ["Windows Update"]);

        Assert.Contains(preview.Rows, r => r is { Scope: "app", Key: "theme", WillApply: true, ImportedValue: "light" });
        Assert.Contains(preview.Rows, r => r is { Scope: "Windows Update", WillApply: true });
        var skipped = preview.Rows.Single(r => r.Scope == "ASUS Platform Tuning");
        Assert.False(skipped.WillApply);
        Assert.Contains("not available", skipped.SkipReason, StringComparison.Ordinal);
        Assert.Equal(1, preview.SkippedCount);
    }

    [Fact]
    public void Apply_WritesThroughTheService_AndCounts()
    {
        var source = CreateService("src2.json");
        source.SetApp(AppSettingKeys.Theme, "light");
        source.SetModule("Windows Update", "k", "v");
        source.SetModule("Ghost Module", "g", "1");
        var json = SettingsTransfer.BuildExportJson(source, "1.0.0", "SRC");

        var target = CreateService("dst2.json");
        var preview = SettingsTransfer.BuildPreview(target, SettingsTransfer.Parse(json)!, ["Windows Update"]);
        var (applied, skipped) = SettingsTransfer.Apply(target, preview);

        Assert.Equal(1, skipped);
        Assert.True(applied >= 2); // theme + defaults + module key
        Assert.Equal("light", target.GetApp(AppSettingKeys.Theme, "?"));
        Assert.Equal("v", target.GetModule("Windows Update", "k"));
        Assert.Null(target.GetModule("Ghost Module", "g"));
    }
}
