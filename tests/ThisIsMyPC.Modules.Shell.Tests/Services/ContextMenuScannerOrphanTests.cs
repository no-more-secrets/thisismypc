using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ContextMenuScannerOrphanTests
{
    private sealed class FakeShellExtensionService : IShellExtensionService
    {
        private readonly List<ShellExtensionInfo> _handlers = [];
        private readonly HashSet<string> _blockedClsids = new(StringComparer.OrdinalIgnoreCase);

        public void AddHandler(ShellExtensionInfo handler) => _handlers.Add(handler);
        public void AddBlockedClsid(string clsid) => _blockedClsids.Add(clsid);

        public OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers()
            => OperationResult<IReadOnlyList<ShellExtensionInfo>>.Success(_handlers);

        public bool IsBlockedByCLSID(string clsid) => _blockedClsids.Contains(clsid);
        public IReadOnlySet<string> GetBlockedClsids() => _blockedClsids;

        public OperationResult<IReadOnlyList<DragDropHandlerInfo>> EnumerateDragDropHandlers()
            => OperationResult<IReadOnlyList<DragDropHandlerInfo>>.Success([]);
    }

    [Fact]
    public void Scan_handler_with_missing_DLL_is_orphaned()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("OldHandler", "{AAAA-1111}",
            @"HKCR\*\shellex\ContextMenuHandlers\OldHandler", "All files",
            @"C:\__test_nonexistent_dll_12345__.dll", null, true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.True(result[0].IsOrphaned);
        Assert.Contains("DLL not found", result[0].OrphanReason!);
    }

    [Fact]
    public void Scan_handler_with_existing_DLL_is_not_orphaned()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("Shell32Handler", "{BBBB-2222}",
            @"HKCR\*\shellex\ContextMenuHandlers\Shell32Handler", "All files",
            @"C:\Windows\System32\shell32.dll", "Microsoft Corporation", true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.False(result[0].IsOrphaned);
        Assert.Null(result[0].OrphanReason);
    }

    [Fact]
    public void Scan_handler_with_null_DllPath_is_orphaned()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("NoDllHandler", "{CCCC-3333}",
            @"HKCR\*\shellex\ContextMenuHandlers\NoDllHandler", "All files",
            null, null, true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.True(result[0].IsOrphaned);
        Assert.Contains("CLSID", result[0].OrphanReason!);
        Assert.Contains("not registered", result[0].OrphanReason!);
    }

    [Fact]
    public void Scan_orphan_detection_applies_only_to_COM_handlers()
    {
        var fake = new FakeShellExtensionService();
        // COM handler with missing DLL — should be orphaned
        fake.AddHandler(new ShellExtensionInfo("OldHandler", "{AAAA-1111}",
            @"HKCR\*\shellex\ContextMenuHandlers\OldHandler", "All files",
            @"C:\__test_nonexistent_dll_12345__.dll", null, true));

        var staticVerbService = new FakeStaticVerbService();
        var modernService = new FakeModernPackagedService();

        var scanner = new ContextMenuScanner(fake, staticVerbService: staticVerbService, modernPackagedService: modernService);
        var result = scanner.Scan();

        // Only the COM handler should have orphan flag set
        var comHandler = result.FirstOrDefault(h => h.HandlerType == HandlerType.ComHandler);
        Assert.NotNull(comHandler);
        Assert.True(comHandler.IsOrphaned);
    }

    [Fact]
    public void Scan_environment_variable_DLL_path_is_expanded()
    {
        var fake = new FakeShellExtensionService();
        // DLL path with environment variable that resolves to a real file
        fake.AddHandler(new ShellExtensionInfo("SysHandler", "{DDDD-4444}",
            @"HKCR\*\shellex\ContextMenuHandlers\SysHandler", "All files",
            @"%SystemRoot%\System32\shell32.dll", "Microsoft Corporation", true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.False(result[0].IsOrphaned);
    }

    [Fact]
    public void Scan_quoted_DLL_path_is_handled()
    {
        var fake = new FakeShellExtensionService();
        // Quoted path to a known-existing DLL
        fake.AddHandler(new ShellExtensionInfo("QuotedHandler", "{EEEE-5555}",
            @"HKCR\*\shellex\ContextMenuHandlers\QuotedHandler", "All files",
            "\"C:\\Windows\\System32\\shell32.dll\"", "Microsoft Corporation", true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.False(result[0].IsOrphaned);
    }

    [Fact]
    public void Scan_mixed_handlers_orphan_and_valid()
    {
        var fake = new FakeShellExtensionService();
        // Valid handler
        fake.AddHandler(new ShellExtensionInfo("ValidHandler", "{1111-AAAA}",
            @"HKCR\*\shellex\ContextMenuHandlers\ValidHandler", "All files",
            @"C:\Windows\System32\shell32.dll", "Microsoft Corporation", true));
        // Orphaned handler
        fake.AddHandler(new ShellExtensionInfo("OrphanHandler", "{2222-BBBB}",
            @"HKCR\*\shellex\ContextMenuHandlers\OrphanHandler", "All files",
            @"C:\__test_nonexistent_dll_99999__.dll", null, true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Equal(2, result.Count);
        var valid = result.First(h => h.Clsid == "{1111-AAAA}");
        var orphan = result.First(h => h.Clsid == "{2222-BBBB}");
        Assert.False(valid.IsOrphaned);
        Assert.True(orphan.IsOrphaned);
    }

    [Fact]
    public void Scan_orphan_preserves_all_handler_properties()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("OldHandler", "{FFFF-6666}",
            @"HKCR\*\shellex\ContextMenuHandlers\OldHandler", "All files",
            @"C:\__test_nonexistent_dll_67890__.dll", "OldPublisher", false));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.True(result[0].IsOrphaned);
        Assert.Equal("OldHandler", result[0].Name);
        Assert.Equal("{FFFF-6666}", result[0].Clsid);
        Assert.Equal(@"C:\__test_nonexistent_dll_67890__.dll", result[0].DllPath);
        Assert.Equal("OldPublisher", result[0].Publisher);
        Assert.False(result[0].IsEnabled);
    }

    // Minimal fakes for static verb and modern services (return empty lists)
    private sealed class FakeStaticVerbService : IStaticVerbService
    {
        public OperationResult<IReadOnlyList<StaticVerbEntry>> EnumerateStaticVerbs()
            => OperationResult<IReadOnlyList<StaticVerbEntry>>.Success([]);
    }

    private sealed class FakeModernPackagedService : IModernPackagedHandlerService
    {
        public OperationResult<IReadOnlyList<ModernPackagedEntry>> EnumerateModernHandlers()
            => OperationResult<IReadOnlyList<ModernPackagedEntry>>.Success([]);
    }
}
