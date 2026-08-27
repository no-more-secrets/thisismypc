using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

/// <summary>
/// Story 26-4 audit assertions: drift-fragile settings carry informational
/// ReversionVectors on the drift-exposed direction only; everything else stays null.
/// </summary>
public sealed class EnforcementMetadataTests
{
    private static TaskbarSettings DefaultTaskbar => new(
        Alignment: 1, WidgetsEnabled: true, ClassicContextMenu: false, ClassicCommandBar: false);

    private static ContextMenuHandler MakeHandler(bool enabled = true) => new(
        Name: "Test Handler",
        Clsid: "{11111111-2222-3333-4444-555555555555}",
        RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\Test",
        AppliesTo: "All files",
        DllPath: null,
        Publisher: null,
        IsEnabled: enabled);

    private static void AssertInformationalOnly(SettingEnforcement enforcement)
    {
        // Informational metadata must never trigger companion logic in the executor.
        Assert.NotNull(enforcement.ReversionVectors);
        Assert.NotEmpty(enforcement.ReversionVectors!);
        Assert.Null(enforcement.CompanionServices);
        Assert.Null(enforcement.CompanionTasks);
        Assert.Null(enforcement.GPCacheEntries);
        Assert.Null(enforcement.SkuRestriction);
        Assert.False(enforcement.OwnerModeRequired);
        Assert.False(enforcement.AclElevation);
    }

    [Fact]
    public void ClassicContextMenu_Enable_CarriesReversionVectors()
    {
        var change = TaskbarChangeFactory.CreateClassicContextMenuToggle(DefaultTaskbar, enable: true);

        Assert.NotNull(change.Enforcement);
        AssertInformationalOnly(change.Enforcement!);
    }

    [Fact]
    public void ClassicContextMenu_Disable_HasNullEnforcement()
    {
        var change = TaskbarChangeFactory.CreateClassicContextMenuToggle(DefaultTaskbar, enable: false);

        Assert.Null(change.Enforcement);
    }

    [Fact]
    public void ClassicCommandBar_Enable_CarriesReversionVectors()
    {
        var change = TaskbarChangeFactory.CreateCommandBarToggle(DefaultTaskbar, enable: true);

        Assert.NotNull(change.Enforcement);
        AssertInformationalOnly(change.Enforcement!);
    }

    [Fact]
    public void TaskbarAlignmentAndWidgets_HaveNullEnforcement()
    {
        Assert.Null(TaskbarChangeFactory.CreateAlignmentChange(DefaultTaskbar, 0).Enforcement);
        Assert.Null(TaskbarChangeFactory.CreateWidgetsToggle(DefaultTaskbar, enable: false).Enforcement);
    }

    [Fact]
    public void HandlerToggle_Disable_CarriesReversionVectors()
    {
        var changes = ContextMenuChangeFactory.CreateToggle(MakeHandler(), enable: false);

        Assert.All(changes, c =>
        {
            Assert.NotNull(c.Enforcement);
            AssertInformationalOnly(c.Enforcement!);
        });
    }

    [Fact]
    public void HandlerToggle_Enable_HasNullEnforcement()
    {
        var changes = ContextMenuChangeFactory.CreateToggle(MakeHandler(enabled: false), enable: true);

        Assert.All(changes, c => Assert.Null(c.Enforcement));
    }

    [Fact]
    public void BlockedListToggle_Disable_CarriesReversionVectors()
    {
        var change = ContextMenuChangeFactory.CreateBlockedListToggle(MakeHandler(), enable: false);

        Assert.NotNull(change.Enforcement);
        AssertInformationalOnly(change.Enforcement!);
    }

    [Fact]
    public void StaticVerbToggle_Disable_CarriesReversionVectors()
    {
        var handler = MakeHandler() with
        {
            VerbInfo = new StaticVerbInfo(
                VerbName: "open", MuiVerb: null, Icon: null, Position: null, IsExtended: false,
                CommandLine: null, DelegateExecuteClsid: null, IsLegacyDisabled: false,
                AppliesTo: "All files", HasLuaShield: false, IsProgrammaticAccessOnly: false),
        };

        var changes = ContextMenuChangeFactory.CreateStaticVerbToggle(handler, enable: false);

        Assert.All(changes, c =>
        {
            Assert.NotNull(c.Enforcement);
            AssertInformationalOnly(c.Enforcement!);
        });
    }

    [Fact]
    public void OrphanCleanup_HasNullEnforcement()
    {
        var group = ContextMenuChangeFactory.CreateOrphanCleanup(MakeHandler());

        Assert.All(group.Changes, c => Assert.Null(c.Enforcement));
    }

    [Fact]
    public void ClassicCommandBar_Disable_HasNullEnforcement()
    {
        Assert.Null(TaskbarChangeFactory.CreateCommandBarToggle(DefaultTaskbar, enable: false).Enforcement);
    }

    [Fact]
    public void BlockedListToggle_Enable_HasNullEnforcement()
    {
        Assert.Null(ContextMenuChangeFactory.CreateBlockedListToggle(MakeHandler(enabled: false), enable: true).Enforcement);
    }

    [Fact]
    public void StaticVerbToggle_Enable_HasNullEnforcement()
    {
        var handler = MakeHandler(enabled: false) with
        {
            VerbInfo = new StaticVerbInfo(
                VerbName: "open", MuiVerb: null, Icon: null, Position: null, IsExtended: false,
                CommandLine: null, DelegateExecuteClsid: null, IsLegacyDisabled: true,
                AppliesTo: "All files", HasLuaShield: false, IsProgrammaticAccessOnly: false),
        };

        Assert.All(
            ContextMenuChangeFactory.CreateStaticVerbToggle(handler, enable: true),
            c => Assert.Null(c.Enforcement));
    }

    [Fact]
    public void Migration_BlockedListEntry_CarriesReversionVectors_RestoresAreNull()
    {
        var handler = MakeHandler(enabled: false);

        var group = ContextMenuChangeFactory.CreateMigration(handler);

        var blockedEntry = group.Changes[0];
        Assert.NotNull(blockedEntry.Enforcement);
        AssertInformationalOnly(blockedEntry.Enforcement!);
        Assert.All(group.Changes.Skip(1), c => Assert.Null(c.Enforcement));
    }
}
