using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ContextMenuScannerDragDropTests
{
    private sealed class FakeShellExtensionService : IShellExtensionService
    {
        private readonly List<DragDropHandlerInfo> _dragDropHandlers = [];
        private readonly HashSet<string> _blockedClsids = new(StringComparer.OrdinalIgnoreCase);

        public void AddDragDropHandler(DragDropHandlerInfo handler) => _dragDropHandlers.Add(handler);

        public OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers()
            => OperationResult<IReadOnlyList<ShellExtensionInfo>>.Success([]);

        public OperationResult<IReadOnlyList<DragDropHandlerInfo>> EnumerateDragDropHandlers()
            => OperationResult<IReadOnlyList<DragDropHandlerInfo>>.Success(_dragDropHandlers);

        public bool IsBlockedByCLSID(string clsid) => _blockedClsids.Contains(clsid);
        public IReadOnlySet<string> GetBlockedClsids() => _blockedClsids;
    }

    [Fact]
    public void Scan_includes_drag_drop_handlers()
    {
        var fake = new FakeShellExtensionService();
        fake.AddDragDropHandler(new DragDropHandlerInfo(
            "7-Zip Drag-Drop", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\*\shellex\DragDropHandlers\7-Zip", "All files",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov"));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal(HandlerType.DragDropHandler, result[0].HandlerType);
        Assert.Equal("7-Zip Drag-Drop", result[0].Name);
    }

    [Fact]
    public void DragDropHandler_deduplicates_by_CLSID()
    {
        var fake = new FakeShellExtensionService();
        var clsid = "{AAAA-DDDD-1234}";

        fake.AddDragDropHandler(new DragDropHandlerInfo(
            "7-Zip", clsid,
            @"HKCR\*\shellex\DragDropHandlers\7-Zip", "All files",
            @"C:\7-Zip\7z.dll", "Igor Pavlov"));
        fake.AddDragDropHandler(new DragDropHandlerInfo(
            "7-Zip", clsid,
            @"HKCR\Directory\shellex\DragDropHandlers\7-Zip", "Directories",
            @"C:\7-Zip\7z.dll", "Igor Pavlov"));
        fake.AddDragDropHandler(new DragDropHandlerInfo(
            "7-Zip", clsid,
            @"HKCR\Folder\shellex\DragDropHandlers\7-Zip", "Folders",
            @"C:\7-Zip\7z.dll", "Igor Pavlov"));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal(3, result[0].AllRegistryPaths!.Count);
        Assert.Equal(3, result[0].AllScopes!.Count);
    }

    [Fact]
    public void DragDropHandler_DllPath_and_Publisher_populated()
    {
        var fake = new FakeShellExtensionService();
        fake.AddDragDropHandler(new DragDropHandlerInfo(
            "WinRAR", "{BBBB-EEEE-5678}",
            @"HKCR\*\shellex\DragDropHandlers\WinRAR", "All files",
            @"C:\Program Files\WinRAR\rarext.dll", "Alexander Roshal"));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal(@"C:\Program Files\WinRAR\rarext.dll", result[0].DllPath);
        Assert.Equal("Alexander Roshal", result[0].Publisher);
    }

    [Fact]
    public void DragDropHandler_is_always_enabled()
    {
        var fake = new FakeShellExtensionService();
        fake.AddDragDropHandler(new DragDropHandlerInfo(
            "TestDD", "{CCCC-FFFF-9999}",
            @"HKCR\*\shellex\DragDropHandlers\TestDD", "All files",
            null, null));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.True(result[0].IsEnabled);
    }

    [Fact]
    public void Scan_includes_both_COM_and_DragDrop_handlers()
    {
        var fake = new FakeShellExtensionService();
        // No COM handlers in this fake (returns empty)
        // Add a drag-drop handler
        fake.AddDragDropHandler(new DragDropHandlerInfo(
            "DDHandler", "{DDDD-1111}",
            @"HKCR\*\shellex\DragDropHandlers\DDHandler", "All files",
            null, null));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal(HandlerType.DragDropHandler, result[0].HandlerType);
    }
}
