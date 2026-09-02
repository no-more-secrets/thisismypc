using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Startup.Changes;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;

namespace ThisIsMyPC.Modules.Startup.Tests.Changes;

public class AutorunChangeFactoryTests
{
    private static AutorunEntry Entry(AutorunCategory category = AutorunCategory.Explorer, AutorunItemKind kind = AutorunItemKind.RegistryKey, bool enabled = true) => new()
    {
        Category = category,
        Kind = kind,
        Name = "Foo Handler",
        Location = AutorunLocations.BackgroundContextMenuHandlersKey,
        Data = "{CLSID}",
        IsEnabled = enabled,
    };

    [Fact]
    public void CreateToggle_NamesTheItemAndTheStates()
    {
        var change = AutorunChangeFactory.CreateToggle(Entry(), enable: false);

        Assert.Equal("Startup & Services", change.ModuleId);
        Assert.Equal("Explorer: Foo Handler", change.DisplayName);
        Assert.Equal(ChangeValueType.Autorun_State, change.ValueType);
        Assert.Equal("Enabled", change.BeforeValue);
        Assert.Equal("Disabled", change.AfterValue);
        Assert.Equal(ChangeCategory.Disable, change.Category);
        Assert.Equal(RestartRequirement.ExplorerRestart, change.RestartRequirement);
        Assert.StartsWith("autorun:", change.SettingId, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemLocation_RoundTripsThroughAutorunTarget()
    {
        var change = AutorunChangeFactory.CreateToggle(Entry(), enable: false);

        var target = AutorunTarget.TryParse(change.SystemLocation);

        Assert.NotNull(target);
        Assert.Equal(AutorunItemKind.RegistryKey, target.Kind);
        Assert.Equal(AutorunLocations.BackgroundContextMenuHandlersKey, target.Location);
        Assert.Equal("Foo Handler", target.Name);
        Assert.Equal($@"{AutorunLocations.BackgroundContextMenuHandlersKey}\Foo Handler", target.EnabledPath);
        Assert.Equal($@"{AutorunLocations.BackgroundContextMenuHandlersKey}\AutorunsDisabled\Foo Handler", target.DisabledPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("RegistryKey|only-two")]
    [InlineData("Bogus|a|b")]
    [InlineData("RegistryValue||name")]
    public void TryParse_RejectsMalformedLocations(string encoded)
        => Assert.Null(AutorunTarget.TryParse(encoded));

    [Theory]
    [InlineData(AutorunCategory.Logon, RestartRequirement.None)]
    [InlineData(AutorunCategory.Office, RestartRequirement.None)]
    [InlineData(AutorunCategory.ScheduledTasks, RestartRequirement.None)]
    [InlineData(AutorunCategory.Explorer, RestartRequirement.ExplorerRestart)]
    [InlineData(AutorunCategory.Drivers, RestartRequirement.Reboot)]
    [InlineData(AutorunCategory.KnownDlls, RestartRequirement.Reboot)]
    public void RestartFor_FollowsTheCategory(AutorunCategory category, RestartRequirement expected)
        => Assert.Equal(expected, AutorunChangeFactory.RestartFor(category));

    [Fact]
    public void TasksAndServices_UseTheLocationAsTheWholePath()
    {
        var task = new AutorunTarget(AutorunItemKind.ScheduledTask, @"\Acme\Updater", "Updater");
        Assert.Equal(@"\Acme\Updater", task.EnabledPath);
        Assert.Null(task.DisabledPath);

        var service = new AutorunTarget(AutorunItemKind.Service, AutorunLocations.ServicesKey, "Spooler");
        Assert.Equal($@"{AutorunLocations.ServicesKey}\Spooler", service.EnabledPath);
        Assert.Null(service.DisabledPath);
    }

    [Fact]
    public void Names_WithThePipeCharacterRoundTrip()
    {
        var target = AutorunTarget.TryParse(new AutorunTarget(AutorunItemKind.RegistryKey, AutorunLocations.PrintMonitorsKey, "A|B|C").Encode());

        Assert.NotNull(target);
        Assert.Equal(AutorunLocations.PrintMonitorsKey, target.Location);
        Assert.Equal("A|B|C", target.Name);
    }

    [Fact]
    public void ParseState_ReadsTheWordAndAnOptionalSnapshot()
    {
        Assert.True(AutorunChangeFactory.ParseState("Enabled", out var none));
        Assert.Null(none);
        Assert.False(AutorunChangeFactory.ParseState("disabled", out _));
        Assert.Null(AutorunChangeFactory.ParseState("Maybe", out _));
        Assert.Null(AutorunChangeFactory.ParseState(null, out _));

        var snapshot = new AutorunSnapshot
        {
            Kind = AutorunItemKind.RegistryValue,
            Values = [new("", "Acme", Core.Services.RegistryValueData.FromString(@"C:\Acme\new.exe"))],
        };
        Assert.True(AutorunChangeFactory.ParseState("Enabled;" + snapshot.Serialize(), out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(@"C:\Acme\new.exe", Assert.Single(parsed.Values).Value.Data);
    }

    [Fact]
    public void ReRegisteredEntry_CarriesItsSnapshotInBeforeValueAndSaysSoInTheName()
    {
        var snapshot = new AutorunSnapshot { Kind = AutorunItemKind.RegistryKey, Values = [] };
        var entry = Entry() with { LiveSnapshot = snapshot.Serialize() };

        var change = AutorunChangeFactory.CreateToggle(entry, enable: false);

        Assert.Equal("Enabled;" + snapshot.Serialize(), change.BeforeValue);
        Assert.Equal("Disabled", change.AfterValue);
        Assert.Equal("Explorer: Foo Handler (re-registered copy)", change.DisplayName);
    }

    [Fact]
    public void ParkedRows_GetTheirOwnSettingId()
    {
        var live = Entry(enabled: true);
        var parked = Entry(enabled: false);

        Assert.NotEqual(AutorunChangeFactory.GetSettingId(live), AutorunChangeFactory.GetSettingId(parked));
        Assert.EndsWith(AutorunChangeFactory.ParkedSuffix, AutorunChangeFactory.GetSettingId(parked), StringComparison.Ordinal);

        // Tasks and services flip in place, so their id never changes with state.
        var task = Entry(AutorunCategory.ScheduledTasks, AutorunItemKind.ScheduledTask, enabled: false);
        Assert.DoesNotContain(AutorunChangeFactory.ParkedSuffix, AutorunChangeFactory.GetSettingId(task), StringComparison.Ordinal);
    }
}
