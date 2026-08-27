using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Core.Tests.Sets;

public sealed class SetProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tipc-sets-{Guid.NewGuid():N}");
    private readonly string _builtIn;
    private readonly string _user;

    public SetProviderTests()
    {
        _builtIn = Path.Combine(_root, "builtin");
        _user = Path.Combine(_root, "user");
        Directory.CreateDirectory(_builtIn);
        Directory.CreateDirectory(_user);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private SetProvider CreateSut() => new(_builtIn, _user);

    private static void WriteSet(string directory, string fileName, string json)
        => File.WriteAllText(Path.Combine(directory, fileName), json);

    private const string ValidSetJson = """
        {
          "name": "Privacy Baseline",
          "description": "Disables tracking basics.",
          "category": "TweakSet",
          "version": "1.0.0",
          "author": "ThisIsMyPC",
          "entries": [
            {
              "moduleId": "Annoyances",
              "settingId": "advertising-id",
              "value": "0",
              "displayValue": "Disabled",
              "description": "Stops advertising ID tracking.",
              "enforcement": {
                "companionServices": ["DiagTrack"],
                "reversionVectors": ["Windows feature updates"],
                "skuRestriction": "Home"
              }
            },
            {
              "moduleId": "Explorer",
              "settingId": "taskbar-widgets",
              "value": "0",
              "description": "Hides taskbar widgets.",
              "group": "Debloat"
            }
          ]
        }
        """;

    [Fact]
    public void ValidSet_LoadsWithAllFields()
    {
        WriteSet(_builtIn, "privacy.json", ValidSetJson);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Warnings);
        var set = Assert.Single(result.Sets);
        Assert.Equal("Privacy Baseline", set.Name);
        Assert.Equal(SetCategory.TweakSet, set.Category);
        Assert.Equal("1.0.0", set.Version);
        Assert.Equal("ThisIsMyPC", set.Author);
        Assert.Equal(SetSource.BuiltIn, set.Source);
        Assert.Equal(Path.Combine(_builtIn, "privacy.json"), set.FilePath);
        Assert.Equal(2, set.Entries.Count);

        var first = set.Entries[0];
        Assert.Equal("Annoyances", first.ModuleId);
        Assert.Equal("advertising-id", first.SettingId);
        Assert.Equal("0", first.Value);
        Assert.Equal("Disabled", first.DisplayValue);
        Assert.NotNull(first.Enforcement);
        Assert.Equal(["DiagTrack"], first.Enforcement!.CompanionServices);
        Assert.Equal(["Windows feature updates"], first.Enforcement.ReversionVectors);
        Assert.Equal(WindowsSku.Home, first.Enforcement.SkuRestriction);

        var second = set.Entries[1];
        Assert.Null(second.Enforcement);
        Assert.Equal("Debloat", second.Group);
    }

    [Fact]
    public void InvalidJson_SkippedWithWarning_SiblingsStillLoad()
    {
        WriteSet(_builtIn, "broken.json", "{ this is not json");
        WriteSet(_builtIn, "valid.json", ValidSetJson);

        var result = CreateSut().LoadSets();

        Assert.Single(result.Sets);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("broken.json", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRequiredFields_SkippedWithWarning()
    {
        WriteSet(_user, "incomplete.json", """
            { "name": "No entries", "description": "d", "category": "TweakSet", "version": "1", "author": "a" }
            """);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Sets);
        Assert.Contains("entries", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void EntryMissingRequiredField_WholeSetSkipped()
    {
        WriteSet(_user, "badentry.json", """
            {
              "name": "Bad entry", "description": "d", "category": "TweakSet",
              "version": "1", "author": "a",
              "entries": [ { "moduleId": "Explorer", "value": "0", "description": "no settingId" } ]
            }
            """);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Sets);
        Assert.Contains("entry 0", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCategory_SkippedWithWarning()
    {
        WriteSet(_user, "badcat.json", """
            {
              "name": "n", "description": "d", "category": "MegaPack", "version": "1", "author": "a",
              "entries": [ { "moduleId": "m", "settingId": "s", "value": "v", "description": "d" } ]
            }
            """);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Sets);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void UserDirectorySets_TaggedAsUser()
    {
        WriteSet(_user, "mine.json", ValidSetJson);

        var result = CreateSut().LoadSets();

        Assert.Equal(SetSource.User, Assert.Single(result.Sets).Source);
    }

    [Fact]
    public void MissingUserDirectory_IsNotAWarning()
    {
        Directory.Delete(_user, recursive: true);
        WriteSet(_builtIn, "valid.json", ValidSetJson);

        var result = CreateSut().LoadSets();

        Assert.Single(result.Sets);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void MissingBuiltInDirectory_ProducesWarning()
    {
        Directory.Delete(_builtIn, recursive: true);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Sets);
        Assert.Contains("Built-in", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownProperties_AreIgnored()
    {
        WriteSet(_user, "future.json", """
            {
              "name": "Future", "description": "d", "category": "OptimizationPack",
              "version": "1", "author": "a", "futureField": { "nested": true },
              "entries": [
                { "moduleId": "m", "settingId": "s", "value": "v", "description": "d", "futureFlag": 7 }
              ]
            }
            """);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Warnings);
        Assert.Equal(SetCategory.OptimizationPack, Assert.Single(result.Sets).Category);
    }

    [Theory]
    [InlineData("\"category\": 99", "category")]
    [InlineData("\"category\": \"TweakSet\"", "skuRestriction")]
    public void UndefinedNumericEnums_SkippedWithWarning(string categoryJson, string expectedProblem)
    {
        // JsonStringEnumConverter accepts raw integers — out-of-range values must be
        // rejected by validation, not silently loaded (a bogus skuRestriction would
        // defeat SKU gating).
        var sku = expectedProblem == "skuRestriction" ? ", \"enforcement\": { \"skuRestriction\": 42 }" : "";
        WriteSet(_user, "badenum.json", $$"""
            {
              "name": "n", "description": "d", {{categoryJson}}, "version": "1", "author": "a",
              "entries": [ { "moduleId": "m", "settingId": "s", "value": "v", "description": "d"{{sku}} } ]
            }
            """);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Sets);
        Assert.Contains(expectedProblem, Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltInAndUserSets_LoadTogether_BuiltInFirst()
    {
        WriteSet(_builtIn, "a-builtin.json", ValidSetJson);
        WriteSet(_user, "a-user.json", ValidSetJson.Replace("Privacy Baseline", "My Set", StringComparison.Ordinal));

        var result = CreateSut().LoadSets();

        Assert.Equal(2, result.Sets.Count);
        Assert.Equal(SetSource.BuiltIn, result.Sets[0].Source);
        Assert.Equal(SetSource.User, result.Sets[1].Source);
    }

    [Fact]
    public void DuplicateSetNames_ProduceWarning_BothStillLoad()
    {
        WriteSet(_builtIn, "one.json", ValidSetJson);
        WriteSet(_user, "two.json", ValidSetJson);

        var result = CreateSut().LoadSets();

        Assert.Equal(2, result.Sets.Count);
        Assert.Contains("Duplicate set name", Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void FullEnforcement_CommentsAndTrailingCommas_Parse()
    {
        WriteSet(_user, "full.json", """
            {
              // comments are tolerated
              "name": "Full", "description": "d", "category": "TweakSet", "version": "1", "author": "a",
              "entries": [
                {
                  "moduleId": "m", "settingId": "s", "value": "", "description": "empty value is legal",
                  "enforcement": {
                    "companionServices": ["DiagTrack"],
                    "companionTasks": ["\\Microsoft\\Windows\\Application Experience\\ProgramDataUpdater"],
                    "gpCacheEntries": ["Entry1"],
                    "reversionVectors": ["Windows Update"],
                    "skuRestriction": "Enterprise",
                    "ownerModeRequired": true,
                    "aclElevation": true,
                  },
                },
              ],
            }
            """);

        var result = CreateSut().LoadSets();

        Assert.Empty(result.Warnings);
        var enforcement = Assert.Single(Assert.Single(result.Sets).Entries).Enforcement!;
        Assert.Equal(["DiagTrack"], enforcement.CompanionServices);
        Assert.Single(enforcement.CompanionTasks!);
        Assert.Equal(["Entry1"], enforcement.GPCacheEntries);
        Assert.Equal(WindowsSku.Enterprise, enforcement.SkuRestriction);
        Assert.True(enforcement.OwnerModeRequired);
        Assert.True(enforcement.AclElevation);
    }

    [Fact]
    public void NonJsonFiles_AreIgnored()
    {
        File.WriteAllText(Path.Combine(_builtIn, "readme.txt"), "not a set");
        WriteSet(_builtIn, "valid.json", ValidSetJson);

        var result = CreateSut().LoadSets();

        Assert.Single(result.Sets);
        Assert.Empty(result.Warnings);
    }
}
