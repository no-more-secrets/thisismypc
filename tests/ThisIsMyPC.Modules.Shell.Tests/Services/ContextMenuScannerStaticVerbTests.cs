using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ContextMenuScannerStaticVerbTests
{
    private sealed class FakeShellExtensionService : IShellExtensionService
    {
        public OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers()
            => OperationResult<IReadOnlyList<ShellExtensionInfo>>.Success(
                Array.Empty<ShellExtensionInfo>());

        public bool IsBlockedByCLSID(string clsid) => false;
        public IReadOnlySet<string> GetBlockedClsids() => new HashSet<string>();

        public OperationResult<IReadOnlyList<DragDropHandlerInfo>> EnumerateDragDropHandlers()
            => OperationResult<IReadOnlyList<DragDropHandlerInfo>>.Success([]);
    }

    private sealed class FakeStaticVerbService : IStaticVerbService
    {
        private readonly List<StaticVerbEntry> _entries = [];

        public void AddEntry(StaticVerbEntry entry) => _entries.Add(entry);

        public OperationResult<IReadOnlyList<StaticVerbEntry>> EnumerateStaticVerbs()
            => OperationResult<IReadOnlyList<StaticVerbEntry>>.Success(_entries);
    }

    private static StaticVerbEntry MakeEntry(
        string verbName = "testverb",
        string registryPath = @"HKCR\*\shell\testverb",
        string scope = "All files",
        string? muiVerb = null,
        string? commandLine = "test.exe",
        string? delegateExecuteClsid = null,
        bool isLegacyDisabled = false,
        bool isExtended = false,
        bool isProgrammaticAccessOnly = false,
        bool hasDropTarget = false)
    {
        return new StaticVerbEntry(
            VerbName: verbName,
            RegistryPath: registryPath,
            Scope: scope,
            MuiVerb: muiVerb,
            Icon: null,
            Position: null,
            IsExtended: isExtended,
            CommandLine: commandLine,
            DelegateExecuteClsid: delegateExecuteClsid,
            HasDropTarget: hasDropTarget,
            IsLegacyDisabled: isLegacyDisabled,
            AppliesTo: null,
            HasLuaShield: false,
            IsProgrammaticAccessOnly: isProgrammaticAccessOnly);
    }

    [Fact]
    public void Scan_includes_static_verbs_with_COM_handlers()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry());

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal(HandlerType.StaticVerb, result[0].HandlerType);
    }

    [Fact]
    public void Scan_static_verb_has_empty_clsid()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry());

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(string.Empty, result[0].Clsid);
    }

    [Fact]
    public void Scan_static_verb_uses_MuiVerb_as_Name_when_available()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(verbName: "AnyCode", muiVerb: "Open with Code"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal("Open with Code", result[0].Name);
    }

    [Fact]
    public void Scan_static_verb_uses_VerbName_when_no_MuiVerb()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(verbName: "cmd"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal("cmd", result[0].Name);
    }

    [Fact]
    public void Scan_deduplicates_same_verb_at_multiple_scopes()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();

        // Same verb at Directory\shell and Directory\Background\shell
        verbService.AddEntry(MakeEntry(
            verbName: "AnyCode",
            registryPath: @"HKCR\Directory\shell\AnyCode",
            scope: "Directories",
            commandLine: @"code.exe ""%V"""));
        verbService.AddEntry(MakeEntry(
            verbName: "AnyCode",
            registryPath: @"HKCR\Directory\Background\shell\AnyCode",
            scope: "Folder background",
            commandLine: @"code.exe ""%V"""));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal(2, result[0].AllRegistryPaths!.Count);
        Assert.Equal(2, result[0].AllScopes!.Count);
    }

    [Fact]
    public void Scan_does_not_deduplicate_different_verbs_with_same_name_different_command()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();

        verbService.AddEntry(MakeEntry(
            verbName: "open",
            registryPath: @"HKCR\*\shell\open",
            commandLine: "notepad.exe"));
        verbService.AddEntry(MakeEntry(
            verbName: "open",
            registryPath: @"HKCR\Directory\shell\open",
            commandLine: "explorer.exe"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Scan_static_verb_IsEnabled_false_when_LegacyDisabled()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(isLegacyDisabled: true));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.False(result[0].IsEnabled);
    }

    [Fact]
    public void Scan_static_verb_has_VerbInfo_populated()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(
            verbName: "AnyCode",
            muiVerb: "Open with Code",
            commandLine: @"code.exe ""%V""",
            isExtended: true));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.NotNull(result[0].VerbInfo);
        Assert.Equal("AnyCode", result[0].VerbInfo!.VerbName);
        Assert.Equal("Open with Code", result[0].VerbInfo!.MuiVerb);
        Assert.True(result[0].VerbInfo!.IsExtended);
    }

    [Fact]
    public void Scan_classifies_canonical_verbs_as_Critical()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(verbName: "open", commandLine: "explorer.exe"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.Critical, result[0].Classification);
    }

    [Fact]
    public void Scan_classifies_open_as_Critical()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(verbName: "open", commandLine: "notepad.exe"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.Critical, result[0].Classification);
    }

    [Fact]
    public void Scan_classifies_print_as_Critical()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(verbName: "print", commandLine: "print.exe"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.Critical, result[0].Classification);
    }

    [Fact]
    public void Scan_classifies_explore_as_Critical()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(verbName: "explore", commandLine: "explorer.exe"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.Critical, result[0].Classification);
    }

    [Fact]
    public void Scan_classifies_properties_as_Critical()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(verbName: "properties", commandLine: "explorer.exe"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.Critical, result[0].Classification);
    }

    [Fact]
    public void Scan_classifies_third_party_verb_correctly()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(
            verbName: "WizTree",
            commandLine: @"C:\Program Files\WizTree\WizTree.exe"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.ThirdParty, result[0].Classification);
    }

    [Fact]
    public void Scan_classifies_DelegateExecute_verb_with_no_command_as_System()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();
        verbService.AddEntry(MakeEntry(
            verbName: "opennewtab",
            commandLine: null,
            delegateExecuteClsid: "{11dbb47c-a525-400b-9e80-a54615a090c0}"));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.System, result[0].Classification);
    }

    [Fact]
    public void Scan_static_verb_per_path_enabled_states_tracks_LegacyDisable()
    {
        var comService = new FakeShellExtensionService();
        var verbService = new FakeStaticVerbService();

        verbService.AddEntry(MakeEntry(
            verbName: "AnyCode",
            registryPath: @"HKCR\Directory\shell\AnyCode",
            scope: "Directories",
            commandLine: @"code.exe ""%V""",
            isLegacyDisabled: false));
        verbService.AddEntry(MakeEntry(
            verbName: "AnyCode",
            registryPath: @"HKCR\Directory\Background\shell\AnyCode",
            scope: "Folder background",
            commandLine: @"code.exe ""%V""",
            isLegacyDisabled: true));

        var scanner = new ContextMenuScanner(comService, staticVerbService: verbService);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.False(result[0].IsEnabled); // Any path disabled = overall disabled
        Assert.True(result[0].PathEnabledStates![@"HKCR\Directory\shell\AnyCode"]);
        Assert.False(result[0].PathEnabledStates![@"HKCR\Directory\Background\shell\AnyCode"]);
    }
}
