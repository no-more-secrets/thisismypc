using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class ContextMenuViewModelSummaryTests : IDisposable
{
    private readonly PendingChangesService _pendingService = new();
    private ContextMenuViewModel? _vm;

    private static ContextMenuHandler MakeComHandler(string name, string clsid, bool isDualRegistered = false) =>
        new(Name: name, Clsid: clsid,
            RegistryPath: $@"HKCR\*\shellex\ContextMenuHandlers\{name}",
            AppliesTo: "All files", DllPath: null, Publisher: null, IsEnabled: true,
            AllScopes: ["All files"],
            HandlerType: HandlerType.ComHandler,
            IsDualRegistered: isDualRegistered);

    private static ContextMenuHandler MakeStaticVerb(string name) =>
        new(Name: name, Clsid: string.Empty,
            RegistryPath: $@"HKCR\*\shell\{name}",
            AppliesTo: "All files", DllPath: null, Publisher: null, IsEnabled: true,
            AllScopes: ["All files"],
            HandlerType: HandlerType.StaticVerb,
            VerbInfo: new StaticVerbInfo(name, null, null, null, false, "cmd.exe", null, false, null, false, false));

    private static ContextMenuHandler MakeModernHandler(string name, string clsid) =>
        new(Name: name, Clsid: clsid,
            RegistryPath: $"PackagedCom\\Test\\{clsid}",
            AppliesTo: "All files", DllPath: null, Publisher: "TestPub", IsEnabled: true,
            AllScopes: ["All files"],
            HandlerType: HandlerType.ModernPackaged,
            PackagedInfo: new ModernPackagedInfo("TestPkg", name, "TestPub", ["*"], null, null));

    private static ContextMenuHandler MakeDragDropHandler(string name, string clsid) =>
        new(Name: name, Clsid: clsid,
            RegistryPath: $@"HKCR\*\shellex\DragDropHandlers\{name}",
            AppliesTo: "All files", DllPath: null, Publisher: null, IsEnabled: true,
            AllScopes: ["All files"],
            HandlerType: HandlerType.DragDropHandler);

    // === Task 8.2: Scan summary count tests ===

    [Fact]
    public void ScanSummary_counts_COM_handlers()
    {
        var registry = new Fakes.FakeRegistryService();
        _vm = new ContextMenuViewModel(
            [MakeComHandler("A", "{1111}"), MakeComHandler("B", "{2222}")],
            _pendingService, registry);

        Assert.Equal(2, _vm.ComHandlerCount);
    }

    [Fact]
    public void ScanSummary_counts_static_verbs()
    {
        var registry = new Fakes.FakeRegistryService();
        _vm = new ContextMenuViewModel(
            [MakeStaticVerb("edit"), MakeStaticVerb("open"), MakeStaticVerb("print")],
            _pendingService, registry);

        Assert.Equal(3, _vm.StaticVerbCount);
    }

    [Fact]
    public void ScanSummary_counts_modern_packaged()
    {
        var registry = new Fakes.FakeRegistryService();
        _vm = new ContextMenuViewModel(
            [MakeModernHandler("WT", "{AAAA}")],
            _pendingService, registry);

        Assert.Equal(1, _vm.ModernPackagedCount);
    }

    [Fact]
    public void ScanSummary_counts_dual_registered()
    {
        var registry = new Fakes.FakeRegistryService();
        _vm = new ContextMenuViewModel(
            [MakeComHandler("A", "{1111}", isDualRegistered: true),
             MakeComHandler("B", "{2222}")],
            _pendingService, registry);

        Assert.Equal(1, _vm.DualRegisteredCount);
    }

    [Fact]
    public void ScanSummary_counts_drag_drop()
    {
        var registry = new Fakes.FakeRegistryService();
        _vm = new ContextMenuViewModel(
            [MakeDragDropHandler("7-Zip", "{DDDD}")],
            _pendingService, registry);

        Assert.Equal(1, _vm.DragDropHandlerCount);
    }

    [Fact]
    public void ScanSummary_string_contains_all_counts()
    {
        var registry = new Fakes.FakeRegistryService();
        _vm = new ContextMenuViewModel(
            [MakeComHandler("A", "{1111}"),
             MakeStaticVerb("edit"),
             MakeModernHandler("WT", "{MMMM}"),
             MakeDragDropHandler("DD", "{DDDD}")],
            _pendingService, registry);

        var summary = _vm.ScanSummary;
        Assert.Contains("1 COM handlers", summary);
        Assert.Contains("1 static verbs", summary);
        Assert.Contains("1 modern", summary);
        Assert.Contains("1 drag-drop", summary);
        Assert.Contains("0 orphaned", summary);
        Assert.Contains("0 dual-registered", summary);
    }

    // === Task 8.4: Classic menu shim detection tests ===

    [Fact]
    public void IsClassicMenuActive_true_when_shim_key_exists_with_empty_default()
    {
        var registry = new ClassicMenuFakeRegistry(shimKeyExists: true, shimValueEmpty: true);
        _vm = new ContextMenuViewModel([], _pendingService, registry);

        Assert.True(_vm.IsClassicMenuActive);
        Assert.NotEmpty(_vm.ClassicMenuBannerText);
        Assert.Contains("Classic menu mode", _vm.ClassicMenuBannerText);
    }

    [Fact]
    public void IsClassicMenuActive_false_when_shim_key_absent()
    {
        var registry = new ClassicMenuFakeRegistry(shimKeyExists: false);
        _vm = new ContextMenuViewModel([], _pendingService, registry);

        Assert.False(_vm.IsClassicMenuActive);
        Assert.Empty(_vm.ClassicMenuBannerText);
    }

    // === Task 8.5: Content-inspecting handler detection tests ===

    [Fact]
    public void ContentInspecting_handler_has_performance_warning()
    {
        var handler = new ContextMenuHandler(
            Name: "WMP Legacy",
            Clsid: "{AAAA-BBBB}",
            RegistryPath: @"HKCR\SystemFileAssociations\Directory.Audio\shellex\ContextMenuHandlers\WMP",
            AppliesTo: "Audio folders",
            DllPath: null, Publisher: null, IsEnabled: true,
            AllScopes: ["Audio folders"],
            IsContentInspecting: true);

        var pendingService = new PendingChangesService();
        var vm = new ContextMenuHandlerViewModel(handler, pendingService);

        Assert.Contains("synchronous file I/O", vm.WarningText);
        Assert.Contains("menu delays", vm.WarningText);

        vm.Dispose();
    }

    [Fact]
    public void Normal_handler_has_no_content_inspecting_warning()
    {
        var handler = new ContextMenuHandler(
            Name: "7-Zip",
            Clsid: "{CCCC-DDDD}",
            RegistryPath: @"HKCR\*\shellex\ContextMenuHandlers\7-Zip",
            AppliesTo: "All files",
            DllPath: null, Publisher: null, IsEnabled: true,
            AllScopes: ["All files"],
            IsContentInspecting: false);

        var pendingService = new PendingChangesService();
        var vm = new ContextMenuHandlerViewModel(handler, pendingService);

        Assert.DoesNotContain("synchronous", vm.WarningText);

        vm.Dispose();
    }

    public void Dispose() => _vm?.Dispose();

    /// <summary>
    /// Specialized fake that controls classic menu shim detection behavior.
    /// </summary>
    private sealed class ClassicMenuFakeRegistry : IRegistryService
    {
        private readonly bool _shimKeyExists;
        private readonly bool _shimValueEmpty;

        public ClassicMenuFakeRegistry(bool shimKeyExists, bool shimValueEmpty = false)
        {
            _shimKeyExists = shimKeyExists;
            _shimValueEmpty = shimValueEmpty;
        }

        public OperationResult<bool> KeyExists(string keyPath)
        {
            if (keyPath.Contains("{86ca1aa0", StringComparison.OrdinalIgnoreCase))
                return OperationResult<bool>.Success(_shimKeyExists);
            return OperationResult<bool>.Success(false);
        }

        public OperationResult<string> ReadString(string keyPath, string valueName)
        {
            if (keyPath.Contains("{86ca1aa0", StringComparison.OrdinalIgnoreCase) && valueName == string.Empty)
                return _shimValueEmpty
                    ? OperationResult<string>.Success(string.Empty)
                    : OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
            return OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
        }

        // Minimal stubs for unused methods
        public OperationResult<int> ReadDWord(string k, string v) => OperationResult<int>.Failure("", ErrorCategory.NotFound);
        public OperationResult<string> ReadExpandString(string k, string v) => OperationResult<string>.Failure("", ErrorCategory.NotFound);
        public OperationResult<string[]> ReadMultiString(string k, string v) => OperationResult<string[]>.Failure("", ErrorCategory.NotFound);
        public OperationResult<bool> WriteDWord(string k, string v, int val) => OperationResult<bool>.Success(true);
        public OperationResult<bool> WriteString(string k, string v, string val) => OperationResult<bool>.Success(true);
        public OperationResult<bool> WriteExpandString(string k, string v, string val) => OperationResult<bool>.Success(true);
        public OperationResult<bool> WriteMultiString(string k, string v, string[] val) => OperationResult<bool>.Success(true);
        public OperationResult<bool> DeleteValue(string k, string v) => OperationResult<bool>.Success(true);
        public OperationResult<bool> DeleteKey(string k, bool r = false) => OperationResult<bool>.Success(true);
        public OperationResult<bool> ValueExists(string k, string v) => OperationResult<bool>.Success(false);
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string k) => OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string k) => OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<string> ReadValueBeforeWrite(string k, string v) => OperationResult<string>.Failure("", ErrorCategory.NotFound);
    }
}
