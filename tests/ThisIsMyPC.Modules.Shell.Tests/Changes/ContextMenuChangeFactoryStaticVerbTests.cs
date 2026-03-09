using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class ContextMenuChangeFactoryStaticVerbTests
{
    private static ContextMenuHandler MakeStaticVerbHandler(
        string verbName = "AnyCode",
        string? muiVerb = "Open with Code",
        bool isEnabled = true,
        IReadOnlyList<string>? allRegistryPaths = null,
        IReadOnlyDictionary<string, bool>? pathEnabledStates = null)
    {
        var registryPath = @"HKCR\*\shell\AnyCode";
        allRegistryPaths ??= [registryPath];
        pathEnabledStates ??= new Dictionary<string, bool> { [registryPath] = isEnabled };

        return new ContextMenuHandler(
            Name: muiVerb ?? verbName,
            Clsid: string.Empty,
            RegistryPath: registryPath,
            AppliesTo: "All files",
            DllPath: null,
            Publisher: null,
            IsEnabled: isEnabled,
            AllRegistryPaths: allRegistryPaths,
            PathEnabledStates: pathEnabledStates,
            HandlerType: HandlerType.StaticVerb,
            VerbInfo: new StaticVerbInfo(
                VerbName: verbName,
                MuiVerb: muiVerb,
                Icon: null,
                Position: null,
                IsExtended: false,
                CommandLine: @"C:\Program Files\VS Code\code.exe ""%1""",
                DelegateExecuteClsid: null,
                IsLegacyDisabled: !isEnabled,
                AppliesTo: null,
                HasLuaShield: false,
                IsProgrammaticAccessOnly: false));
    }

    [Fact]
    public void CreateStaticVerbToggle_disable_writes_LegacyDisable_empty_string()
    {
        var handler = MakeStaticVerbHandler(isEnabled: true);

        var changes = ContextMenuChangeFactory.CreateStaticVerbToggle(handler, enable: false);

        var change = Assert.Single(changes);
        Assert.Equal(ChangeValueType.Registry_String, change.ValueType);
        Assert.Equal(@"HKCR\*\shell\AnyCode\LegacyDisable", change.SystemLocation);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue); // was absent (enabled)
        Assert.Equal("", change.AfterValue); // write empty string
        Assert.Equal(ChangeCategory.Disable, change.Category);
    }

    [Fact]
    public void CreateStaticVerbToggle_enable_deletes_LegacyDisable()
    {
        var handler = MakeStaticVerbHandler(isEnabled: false);

        var changes = ContextMenuChangeFactory.CreateStaticVerbToggle(handler, enable: true);

        var change = Assert.Single(changes);
        Assert.Equal(@"HKCR\*\shell\AnyCode\LegacyDisable", change.SystemLocation);
        Assert.Equal("", change.BeforeValue); // was present (disabled)
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.AfterValue); // delete it
        Assert.Equal(ChangeCategory.Enable, change.Category);
    }

    [Fact]
    public void CreateStaticVerbToggle_multi_path_produces_one_descriptor_per_path()
    {
        var paths = new List<string>
        {
            @"HKCR\Directory\shell\AnyCode",
            @"HKCR\Directory\Background\shell\AnyCode",
        };
        var states = new Dictionary<string, bool>
        {
            [paths[0]] = true,
            [paths[1]] = true,
        };

        var handler = MakeStaticVerbHandler(
            isEnabled: true,
            allRegistryPaths: paths,
            pathEnabledStates: states);

        var changes = ContextMenuChangeFactory.CreateStaticVerbToggle(handler, enable: false);

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.SystemLocation == @"HKCR\Directory\shell\AnyCode\LegacyDisable");
        Assert.Contains(changes, c => c.SystemLocation == @"HKCR\Directory\Background\shell\AnyCode\LegacyDisable");
    }

    [Fact]
    public void CreateStaticVerbToggle_all_share_same_SettingId()
    {
        var paths = new List<string>
        {
            @"HKCR\Directory\shell\AnyCode",
            @"HKCR\Directory\Background\shell\AnyCode",
        };
        var states = new Dictionary<string, bool>
        {
            [paths[0]] = true,
            [paths[1]] = true,
        };

        var handler = MakeStaticVerbHandler(
            isEnabled: true,
            allRegistryPaths: paths,
            pathEnabledStates: states);

        var changes = ContextMenuChangeFactory.CreateStaticVerbToggle(handler, enable: false);

        var settingIds = changes.Select(c => c.SettingId).Distinct().ToList();
        Assert.Single(settingIds);
    }

    [Fact]
    public void MakeStaticVerbSettingId_produces_expected_format()
    {
        var id = ContextMenuChangeFactory.MakeStaticVerbSettingId("AnyCode", "All files");
        Assert.Equal("ctx-verb-anycode-all-files", id);
    }
}
