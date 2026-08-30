using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class WindowsEntriesTests
{
    private readonly FakeRegistryService _registry = new();

    private ContextMenuModule Module => new(
        _registry, new ShellExtensionService(_registry), new NullContextMenuProbe());

    private string? GetString(string keyPath, string valueName) =>
        _registry.ReadString(keyPath, valueName) is { IsSuccess: true } r ? r.Value : null;

    private int? GetDWord(string keyPath, string valueName) =>
        _registry.ReadDWord(keyPath, valueName) is { IsSuccess: true } r ? r.Value : null;

    private byte[]? GetBinary(string keyPath, string valueName) =>
        _registry.ReadBinary(keyPath, valueName) is { IsSuccess: true } r ? r.Value : null;

    private static WindowsMenuEntry Entry(string settingId) =>
        WindowsEntriesChangeFactory.Catalog.Single(e => e.SettingId == settingId);

    [Fact]
    public void Catalog_SettingIdsAreUniqueAndModuleIdMatches()
    {
        var ids = WindowsEntriesChangeFactory.Catalog.Select(e => e.SettingId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(9, ids.Count);

        foreach (var entry in WindowsEntriesChangeFactory.Catalog)
        {
            var group = entry.CreateToggle(new FakeRegistryService(), true);
            Assert.All(group.Changes, c => Assert.Equal("Context Menus", c.ModuleId));
            Assert.All(group.Changes, c => Assert.Equal(entry.SettingId, c.SettingId));
        }
    }

    [Fact]
    public async Task MsiExtract_EnableMaterializesTreeUnderHkcuOverlay()
    {
        var group = Entry("ctx-win-msi-extract").CreateToggle(_registry, true);

        var result = await Module.ApplyChangeAsync(group.Changes[0]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("@shell32.dll,-37514",
            GetString(WindowsEntriesChangeFactory.MsiExtractKeyPath, "MUIVerb"));
        Assert.Contains("msiexec.exe /a",
            GetString($@"{WindowsEntriesChangeFactory.MsiExtractKeyPath}\Command", ""));
    }

    [Fact]
    public async Task MsiExtract_DisableDeletesTree_AndIsIdempotent()
    {
        var enable = Entry("ctx-win-msi-extract").CreateToggle(_registry, true);
        await Module.ApplyChangeAsync(enable.Changes[0]);

        var disable = Entry("ctx-win-msi-extract").CreateToggle(_registry, false);
        var result = await Module.ApplyChangeAsync(disable.Changes[0]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Null(GetString(WindowsEntriesChangeFactory.MsiExtractKeyPath, "MUIVerb"));

        // Deleting again succeeds (key already absent).
        var again = await Module.ApplyChangeAsync(disable.Changes[0]);
        Assert.True(again.IsSuccess);
    }

    [Fact]
    public async Task KeyTree_RejectsNonAllowlistedPath()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Context Menus",
            SettingId = "evil",
            DisplayName = "Evil",
            SystemLocation = @"HKCU\Software\Classes\exefile\shell\open",
            BeforeValue = ShellRegistryPaths.AbsentValue,
            AfterValue = new RegistryKeyTreeDefinition { Values = [] }.Serialize(),
            BeforeDisplay = "Hidden",
            AfterDisplay = "Shown",
            ValueType = ChangeValueType.Registry_KeyTree,
        };

        var result = await Module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task NewZip_DisableDeletesHkcrKey_EnableRestoresBinaryAndItemName()
    {
        _registry.AddKey(WindowsEntriesChangeFactory.ZipShellNewKeyPath);
        Assert.True(Entry("ctx-win-new-zip").ReadState(_registry));

        var disable = Entry("ctx-win-new-zip").CreateToggle(_registry, false);
        var result = await Module.ApplyChangeAsync(disable.Changes[0]);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(Entry("ctx-win-new-zip").ReadState(_registry));

        var enable = Entry("ctx-win-new-zip").CreateToggle(_registry, true);
        result = await Module.ApplyChangeAsync(enable.Changes[0]);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var data = GetBinary(WindowsEntriesChangeFactory.ZipShellNewKeyPath, "Data");
        Assert.NotNull(data);
        Assert.Equal(0x50, data![0]); // 'P'
        Assert.Equal(0x4B, data[1]);  // 'K'
    }

    [Fact]
    public async Task NewZip_UndoRestoresThirdPartyValuesFromLiveSnapshot()
    {
        // A value some other tool put on the key must survive our delete+undo.
        _registry.AddKey(WindowsEntriesChangeFactory.ZipShellNewKeyPath);
        _registry.SetString(WindowsEntriesChangeFactory.ZipShellNewKeyPath, "ThirdParty", "keep-me");

        var disable = Entry("ctx-win-new-zip").CreateToggle(_registry, false).Changes[0];
        await Module.ApplyChangeAsync(disable);
        Assert.Null(GetString(WindowsEntriesChangeFactory.ZipShellNewKeyPath, "ThirdParty"));

        var swapped = disable with
        {
            BeforeValue = disable.AfterValue ?? string.Empty,
            AfterValue = disable.BeforeValue,
        };
        var result = await Module.RevertChangeAsync(swapped);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("keep-me", GetString(WindowsEntriesChangeFactory.ZipShellNewKeyPath, "ThirdParty"));
    }

    [Fact]
    public async Task EditWithPaint_DisableWritesBlockedListValue()
    {
        var group = Entry("ctx-win-edit-paint").CreateToggle(_registry, false);

        var result = await Module.ApplyChangeAsync(group.Changes[0]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("", GetString(
            ShellRegistryPaths.BlockedListKeyPath, WindowsEntriesChangeFactory.PaintEditClsid));
        Assert.False(Entry("ctx-win-edit-paint").ReadState(_registry));
    }

    [Fact]
    public async Task PrintScripts_DisableStagesBothFileTypes()
    {
        var group = Entry("ctx-win-print-scripts").CreateToggle(_registry, false);

        Assert.Equal(2, group.Changes.Count);
        foreach (var change in group.Changes)
        {
            var result = await Module.ApplyChangeAsync(change);
            Assert.True(result.IsSuccess, result.ErrorMessage);
        }

        Assert.Equal("", GetString(@"HKCR\batfile\shell\print", "ProgrammaticAccessOnly"));
        Assert.Equal("", GetString(@"HKCR\cmdfile\shell\print", "ProgrammaticAccessOnly"));
    }

    [Fact]
    public async Task MultiSelectLimit_EnableWritesDword_DisableDeletes()
    {
        const string keyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";
        var enable = Entry("ctx-win-multi-select-verbs").CreateToggle(_registry, true);
        var result = await Module.ApplyChangeAsync(enable.Changes[0]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(300, GetDWord(keyPath, "MultipleInvokePromptMinimum"));
        Assert.True(Entry("ctx-win-multi-select-verbs").ReadState(_registry));

        var disable = Entry("ctx-win-multi-select-verbs").CreateToggle(_registry, false);
        result = await Module.ApplyChangeAsync(disable.Changes[0]);
        Assert.True(result.IsSuccess);
        Assert.Null(GetDWord(keyPath, "MultipleInvokePromptMinimum"));
    }

    [Fact]
    public async Task StoreOpenWith_DisableWritesPolicy_EnableDeletes()
    {
        const string keyPath = @"HKCU\Software\Policies\Microsoft\Windows\Explorer";
        var disable = Entry("ctx-win-store-open-with").CreateToggle(_registry, false);
        var result = await Module.ApplyChangeAsync(disable.Changes[0]);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, GetDWord(keyPath, "NoUseStoreOpenWith"));
        Assert.False(Entry("ctx-win-store-open-with").ReadState(_registry));

        var enable = Entry("ctx-win-store-open-with").CreateToggle(_registry, true);
        result = await Module.ApplyChangeAsync(enable.Changes[0]);
        Assert.True(result.IsSuccess);
        Assert.Null(GetDWord(keyPath, "NoUseStoreOpenWith"));
    }

    [Fact]
    public async Task RoundTrip_SwappedDescriptorRestoresOriginalState()
    {
        // Enable MSI extract, then revert with the standard swapped descriptor.
        var enable = Entry("ctx-win-msi-extract").CreateToggle(_registry, true).Changes[0];
        await Module.ApplyChangeAsync(enable);
        Assert.True(Entry("ctx-win-msi-extract").ReadState(_registry));

        var swapped = enable with
        {
            BeforeValue = enable.AfterValue ?? string.Empty,
            AfterValue = enable.BeforeValue,
        };
        var result = await Module.RevertChangeAsync(swapped);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(Entry("ctx-win-msi-extract").ReadState(_registry));
    }

    private sealed class NullContextMenuProbe : IContextMenuProbe
    {
        public Core.Results.OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
            => Core.Results.OperationResult<bool>.Success(true);
    }
}
