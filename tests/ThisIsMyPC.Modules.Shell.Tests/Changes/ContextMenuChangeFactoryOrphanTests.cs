using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class ContextMenuChangeFactoryOrphanTests
{
    private static ContextMenuHandler MakeOrphanedHandler(
        string name = "OldHandler",
        string clsid = "{AAAA-BBBB-CCCC}",
        IReadOnlyList<string>? allRegistryPaths = null,
        IReadOnlyDictionary<string, bool>? pathEnabledStates = null) =>
        new(
            Name: name,
            Clsid: clsid,
            RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\OldHandler",
            AppliesTo: "All files",
            DllPath: @"C:\missing\old.dll",
            Publisher: null,
            IsEnabled: true,
            AllRegistryPaths: allRegistryPaths,
            AllScopes: ["All files"],
            PathEnabledStates: pathEnabledStates,
            IsOrphaned: true,
            OrphanReason: "DLL not found: C:\\missing\\old.dll");

    [Fact]
    public void CreateOrphanCleanup_produces_one_descriptor_per_path()
    {
        var handler = MakeOrphanedHandler(allRegistryPaths:
        [
            @"HKCR\*\shellex\ContextMenuHandlers\OldHandler",
            @"HKCR\Directory\shellex\ContextMenuHandlers\OldHandler",
        ]);

        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.Equal(2, group.Changes.Count);
    }

    [Fact]
    public void CreateOrphanCleanup_uses_AbsentValue_as_AfterValue()
    {
        var handler = MakeOrphanedHandler();
        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.All(group.Changes, c => Assert.Equal(ShellRegistryPaths.AbsentValue, c.AfterValue));
    }

    [Fact]
    public void CreateOrphanCleanup_stores_CLSID_as_BeforeValue_for_undo()
    {
        var handler = MakeOrphanedHandler(clsid: "{12345678-ABCD-EFGH}");
        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.All(group.Changes, c => Assert.Equal("{12345678-ABCD-EFGH}", c.BeforeValue));
    }

    [Fact]
    public void CreateOrphanCleanup_sets_Category_Delete()
    {
        var handler = MakeOrphanedHandler();
        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.All(group.Changes, c => Assert.Equal(ChangeCategory.Delete, c.Category));
    }

    [Fact]
    public void CreateOrphanCleanup_requires_ExplorerRestart()
    {
        var handler = MakeOrphanedHandler();
        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.All(group.Changes, c => Assert.Equal(RestartRequirement.ExplorerRestart, c.RestartRequirement));
    }

    [Fact]
    public void CreateOrphanCleanup_SystemLocation_targets_Default_value()
    {
        var handler = MakeOrphanedHandler();
        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.All(group.Changes, c => Assert.EndsWith(@"\(Default)", c.SystemLocation));
    }

    [Fact]
    public void CreateOrphanCleanup_DisplayName_contains_handler_name()
    {
        var handler = MakeOrphanedHandler(name: "NvidiaOld");
        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.Contains("NvidiaOld", group.DisplayName);
        Assert.All(group.Changes, c => Assert.Contains("NvidiaOld", c.DisplayName));
    }

    [Fact]
    public void CreateOrphanCleanup_disabled_path_uses_dash_prefix_BeforeValue()
    {
        var handler = MakeOrphanedHandler(
            clsid: "{DEAD-BEEF}",
            allRegistryPaths:
            [
                @"HKCR\*\shellex\ContextMenuHandlers\OldHandler",
            ],
            pathEnabledStates: new Dictionary<string, bool>
            {
                [@"HKCR\*\shellex\ContextMenuHandlers\OldHandler"] = false,
            });

        var group = ContextMenuChangeFactory.CreateOrphanCleanup(handler);

        Assert.Single(group.Changes);
        Assert.Equal("-{DEAD-BEEF}", group.Changes[0].BeforeValue);
    }

    [Fact]
    public void CreateBulkOrphanCleanup_wraps_all_orphans()
    {
        var orphan1 = MakeOrphanedHandler(name: "Orphan1", clsid: "{1111}");
        var orphan2 = MakeOrphanedHandler(name: "Orphan2", clsid: "{2222}",
            allRegistryPaths:
            [
                @"HKCR\*\shellex\ContextMenuHandlers\Orphan2",
                @"HKCR\Directory\shellex\ContextMenuHandlers\Orphan2",
            ]);

        var group = ContextMenuChangeFactory.CreateBulkOrphanCleanup([orphan1, orphan2]);

        // orphan1 = 1 path, orphan2 = 2 paths → 3 total descriptors
        Assert.Equal(3, group.Changes.Count);
    }

    [Fact]
    public void CreateBulkOrphanCleanup_DisplayName_includes_count()
    {
        var orphan1 = MakeOrphanedHandler(name: "O1", clsid: "{1111}");
        var orphan2 = MakeOrphanedHandler(name: "O2", clsid: "{2222}");

        var group = ContextMenuChangeFactory.CreateBulkOrphanCleanup([orphan1, orphan2]);

        Assert.Contains("2", group.DisplayName);
    }

    [Fact]
    public void CreateBulkOrphanCleanup_GroupId_is_unique()
    {
        var orphan = MakeOrphanedHandler();

        var group1 = ContextMenuChangeFactory.CreateBulkOrphanCleanup([orphan]);
        var group2 = ContextMenuChangeFactory.CreateBulkOrphanCleanup([orphan]);

        Assert.NotEqual(group1.GroupId, group2.GroupId);
    }

    [Fact]
    public void CreateBulkOrphanCleanup_all_descriptors_use_Delete_category()
    {
        var orphan1 = MakeOrphanedHandler(name: "O1", clsid: "{1111}");
        var orphan2 = MakeOrphanedHandler(name: "O2", clsid: "{2222}");

        var group = ContextMenuChangeFactory.CreateBulkOrphanCleanup([orphan1, orphan2]);

        Assert.All(group.Changes, c => Assert.Equal(ChangeCategory.Delete, c.Category));
    }
}
