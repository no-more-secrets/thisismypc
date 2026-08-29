using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class CustomVerbTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly ContextMenuModule _module;

    private static readonly CustomVerbDefinition Definition = new()
    {
        Scope = "Directory",
        VerbId = "open-terminal",
        Label = "Open Terminal Here",
        Command = "wt.exe -d \"%1\"",
        IconPath = @"C:\Tools\wt.exe,0",
    };

    private const string KeyPath = @"HKCU\Software\Classes\Directory\shell\ThisIsMyPC.open-terminal";

    public CustomVerbTests()
    {
        var shellExtSvc = new ShellExtensionService(_registry);
        _module = new ContextMenuModule(_registry, shellExtSvc, new NullContextMenuProbe());
    }

    [Fact]
    public void Definition_round_trips_through_json()
    {
        var restored = CustomVerbDefinition.Deserialize(Definition.Serialize());
        Assert.Equal(Definition, restored);
    }

    [Fact]
    public void CreateNew_targets_the_prefixed_hkcu_verb_key_with_create_category()
    {
        var change = CustomVerbChangeFactory.CreateNew(Definition);

        Assert.Equal(KeyPath, change.SystemLocation);
        Assert.Equal(ChangeCategory.Create, change.Category);
        Assert.Equal(ChangeValueType.Shell_CustomVerb, change.ValueType);
        Assert.Equal(ShellRegistryPaths.AbsentValue, change.BeforeValue);
        Assert.Equal(Definition, CustomVerbDefinition.Deserialize(change.AfterValue));
    }

    [Fact]
    public void CreateEdit_carries_both_definitions_and_rejects_scope_moves()
    {
        var after = Definition with { Label = "Terminal", IconPath = null };
        var change = CustomVerbChangeFactory.CreateEdit(Definition, after);

        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(Definition, CustomVerbDefinition.Deserialize(change.BeforeValue));
        Assert.Equal(after, CustomVerbDefinition.Deserialize(change.AfterValue));

        Assert.Throws<ArgumentException>(() =>
            CustomVerbChangeFactory.CreateEdit(Definition, Definition with { Scope = "*" }));
    }

    [Fact]
    public void MakeVerbId_slugs_labels_safely()
    {
        Assert.Equal("open-terminal-here", CustomVerbChangeFactory.MakeVerbId("Open Terminal Here!"));
        Assert.Equal("entry", CustomVerbChangeFactory.MakeVerbId("!!!"));
    }

    [Fact]
    public async Task Apply_create_materializes_label_icon_and_command()
    {
        var result = await _module.ApplyChangeAsync(CustomVerbChangeFactory.CreateNew(Definition));

        Assert.True(result.IsSuccess);
        Assert.Equal("Open Terminal Here", _registry.ReadString(KeyPath, string.Empty).Value);
        Assert.Equal(@"C:\Tools\wt.exe,0", _registry.ReadString(KeyPath, "Icon").Value);
        Assert.Equal("wt.exe -d \"%1\"", _registry.ReadString($@"{KeyPath}\command", string.Empty).Value);
    }

    [Fact]
    public async Task Apply_edit_removing_icon_deletes_the_stale_icon_value()
    {
        await _module.ApplyChangeAsync(CustomVerbChangeFactory.CreateNew(Definition));

        var after = Definition with { Label = "Terminal", IconPath = null };
        var result = await _module.ApplyChangeAsync(CustomVerbChangeFactory.CreateEdit(Definition, after));

        Assert.True(result.IsSuccess);
        Assert.Equal("Terminal", _registry.ReadString(KeyPath, string.Empty).Value);
        Assert.False(_registry.ValueExists(KeyPath, "Icon").Value);
    }

    [Fact]
    public async Task Apply_delete_removes_the_key_tree_and_tolerates_already_gone()
    {
        await _module.ApplyChangeAsync(CustomVerbChangeFactory.CreateNew(Definition));

        var delete = CustomVerbChangeFactory.CreateDelete(Definition);
        var result = await _module.ApplyChangeAsync(delete);

        Assert.True(result.IsSuccess);
        Assert.False(_registry.KeyExists(KeyPath).Value);
        Assert.False(_registry.KeyExists($@"{KeyPath}\command").Value);

        // Idempotent: deleting an absent entry (e.g. history redo after manual cleanup)
        Assert.True((await _module.ApplyChangeAsync(delete)).IsSuccess);
    }

    [Fact]
    public async Task Apply_refuses_key_paths_outside_the_thisismypc_namespace()
    {
        var foreign = CustomVerbChangeFactory.CreateDelete(Definition) with
        {
            SystemLocation = @"HKCU\Software\Classes\Directory\shell\cmd",
        };

        var result = await _module.ApplyChangeAsync(foreign);

        Assert.False(result.IsSuccess);
        Assert.True(_registry.KeyExists(@"HKCU\Software\Classes\Directory\shell\cmd").IsSuccess);
    }

    [Fact]
    public async Task Revert_of_create_deletes_what_apply_created()
    {
        var create = CustomVerbChangeFactory.CreateNew(Definition);
        await _module.ApplyChangeAsync(create);

        // Pipeline hands revert a Before/After-swapped descriptor
        var swapped = create with { BeforeValue = create.AfterValue!, AfterValue = create.BeforeValue };
        var result = await _module.RevertChangeAsync(swapped);

        Assert.True(result.IsSuccess);
        Assert.False(_registry.KeyExists(KeyPath).Value);
    }

    [Fact]
    public void Enumerate_returns_only_prefixed_entries_and_skips_commandless_keys()
    {
        _registry.SetString(KeyPath, string.Empty, "Open Terminal Here");
        _registry.SetString(KeyPath, "Icon", @"C:\Tools\wt.exe,0");
        _registry.SetString($@"{KeyPath}\command", string.Empty, "wt.exe -d \"%1\"");
        // Foreign verb — must stay invisible
        _registry.SetString(@"HKCU\Software\Classes\Directory\shell\cmd\command", string.Empty, "cmd.exe");
        // Ours but half-written (no command) — not editable, skipped
        _registry.SetString(@"HKCU\Software\Classes\*\shell\ThisIsMyPC.broken", string.Empty, "Broken");

        var entries = new CustomVerbService(_registry).Enumerate();

        var entry = Assert.Single(entries);
        Assert.Equal(Definition, entry);
    }

    private sealed class NullContextMenuProbe : IContextMenuProbe
    {
        public Core.Results.OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
            => Core.Results.OperationResult<bool>.Success(true);
    }
}
