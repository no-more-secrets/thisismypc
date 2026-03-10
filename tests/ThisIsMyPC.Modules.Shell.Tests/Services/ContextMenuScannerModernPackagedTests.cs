using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ContextMenuScannerModernPackagedTests
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
    }

    [Fact]
    public void Scan_includes_modern_handlers_in_result()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler(
                "{9F156763-7844-4DC4-B2B1-901F640F5155}",
                "Windows Terminal",
                "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
                "Windows Terminal",
                "Microsoft Corporation",
                ["Directory", @"Directory\Background"]);

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal(HandlerType.ModernPackaged, result[0].HandlerType);
        Assert.Equal("Windows Terminal", result[0].Name);
    }

    [Fact]
    public void Modern_handler_has_correct_classification()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler(
                "{AAAA-BBBB}",
                "Microsoft App",
                "Microsoft.SomeApp_hash",
                "Microsoft App",
                "Microsoft Corporation");

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.System, result[0].Classification);
    }

    [Fact]
    public void Third_party_modern_handler_classified_as_ThirdParty()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler(
                "{CCCC-DDDD}",
                "NanaZip",
                "40174MouriNaruto.NanaZip_hash",
                "NanaZip",
                "Kenji Mouri");

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.ThirdParty, result[0].Classification);
    }

    [Fact]
    public void Modern_handler_is_always_enabled()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{EEEE-FFFF}", "TestApp", "TestPkg_hash");

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.True(result[0].IsEnabled);
        Assert.Equal(DisableMethod.None, result[0].DisableMethod);
    }

    [Fact]
    public void Modern_handler_has_PackagedInfo_populated()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler(
                "{1111-2222}",
                "PowerToys",
                "Microsoft.PowerToys_hash",
                "PowerToys",
                "Microsoft Corporation",
                ["*", "Directory"],
                "pt-verb");

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.NotNull(result[0].PackagedInfo);
        Assert.Equal("Microsoft.PowerToys_hash", result[0].PackagedInfo!.PackageFamilyName);
        Assert.Equal("PowerToys", result[0].PackagedInfo!.PackageDisplayName);
        Assert.Equal("Microsoft Corporation", result[0].PackagedInfo!.PublisherDisplayName);
        Assert.Equal(["*", "Directory"], result[0].PackagedInfo!.ItemTypes);
        Assert.Equal("pt-verb", result[0].PackagedInfo!.VerbId);
    }

    [Fact]
    public void Scan_maps_ItemType_star_to_AllFiles_scope()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{AAAA}", "TestApp", "Test_hash", itemTypes: ["*"]);

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Contains("All files", result[0].AllScopes!);
    }

    [Fact]
    public void Scan_maps_ItemType_Directory_to_Directories_scope()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{BBBB}", "TestApp", "Test_hash", itemTypes: ["Directory"]);

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Contains("Directories", result[0].AllScopes!);
    }

    [Fact]
    public void Scan_maps_ItemType_DirectoryBackground_to_FolderBackground_scope()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{CCCC}", "TestApp", "Test_hash", itemTypes: [@"Directory\Background"]);

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Contains("Folder background", result[0].AllScopes!);
    }

    [Fact]
    public void Scan_maps_null_ItemTypes_to_UnknownScope()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{DDDD}", "TestApp", "Test_hash", itemTypes: null);

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Contains("Unknown scope", result[0].AllScopes!);
    }

    [Fact]
    public void Cross_type_dedup_detects_same_CLSID_COM_and_Modern()
    {
        var shellSvc = new FakeShellExtensionService();
        shellSvc.AddHandler(new ShellExtensionInfo(
            "PowerToys CM", "{DEDUP-CLSID-1234}",
            @"HKCR\*\shellex\ContextMenuHandlers\PowerToys", "All files",
            @"C:\PowerToys\dll.dll", "Microsoft Corporation", true));

        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler(
                "{DEDUP-CLSID-1234}",
                "PowerToys Modern",
                "Microsoft.PowerToys_hash",
                "PowerToys",
                "Microsoft Corporation",
                ["*"]);

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Equal(2, result.Count);

        var com = result.First(h => h.HandlerType == HandlerType.ComHandler);
        var modern = result.First(h => h.HandlerType == HandlerType.ModernPackaged);

        Assert.True(com.IsDualRegistered);
        Assert.True(modern.IsDualRegistered);
        Assert.Equal("PowerToys Modern", com.DualRegistrationPartnerName);
        Assert.Equal("PowerToys CM", modern.DualRegistrationPartnerName);
    }

    [Fact]
    public void Cross_type_dedup_does_not_link_different_CLSIDs()
    {
        var shellSvc = new FakeShellExtensionService();
        shellSvc.AddHandler(new ShellExtensionInfo(
            "Legacy Handler", "{LEGACY-CLSID}",
            @"HKCR\*\shellex\ContextMenuHandlers\Legacy", "All files",
            @"C:\Legacy\dll.dll", "SomeCo", true));

        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{MODERN-CLSID}", "Modern Handler", "SomeCo.App_hash");

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Equal(2, result.Count);
        Assert.All(result, h => Assert.False(h.IsDualRegistered));
    }

    [Fact]
    public void Scan_works_when_modern_service_returns_failure()
    {
        var shellSvc = new FakeShellExtensionService();
        shellSvc.AddHandler(new ShellExtensionInfo(
            "7-Zip", "{7ZIP}",
            @"HKCR\*\shellex\ContextMenuHandlers\7-Zip", "All files",
            @"C:\7-Zip\7z.dll", "Igor Pavlov", true));

        var modernSvc = new FakeModernPackagedHandlerService();
        modernSvc.SetFailure();

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        // COM handler still present
        Assert.Single(result);
        Assert.Equal(HandlerType.ComHandler, result[0].HandlerType);
    }

    [Fact]
    public void Scan_works_without_modern_service()
    {
        var shellSvc = new FakeShellExtensionService();
        shellSvc.AddHandler(new ShellExtensionInfo(
            "7-Zip", "{7ZIP}",
            @"HKCR\*\shellex\ContextMenuHandlers\7-Zip", "All files",
            @"C:\7-Zip\7z.dll", "Igor Pavlov", true));

        var scanner = new ContextMenuScanner(shellSvc);
        var result = scanner.Scan();

        Assert.Single(result);
    }

    [Fact]
    public void Modern_handler_multiple_scopes_deduplicates()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{MULTI}", "TestApp", "Test_hash",
                itemTypes: ["*", "Directory", "*"]); // duplicated *

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        // Should deduplicate "All files" scope
        Assert.Equal(2, result[0].AllScopes!.Count);
        Assert.Contains("All files", result[0].AllScopes!);
        Assert.Contains("Directories", result[0].AllScopes!);
    }

    [Fact]
    public void Modern_handler_extension_specific_maps_to_AllFiles()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{EXT}", "TestApp", "Test_hash", itemTypes: [".png"]);

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Contains("All files", result[0].AllScopes!);
    }

    [Fact]
    public void Modern_handler_Name_uses_HandlerName_not_PackageDisplayName()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler(
                "{NAME-TEST}",
                "Open in Terminal",         // HandlerName (extension display name)
                "Microsoft.WindowsTerminal_hash",
                "Windows Terminal",          // PackageDisplayName (package name)
                "Microsoft Corporation");

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Equal("Open in Terminal", result[0].Name);
        Assert.Equal("Windows Terminal", result[0].PackagedInfo!.PackageDisplayName);
    }

    [Fact]
    public void Modern_handler_InstallSource_passed_through()
    {
        var shellSvc = new FakeShellExtensionService();
        var modernSvc = new FakeModernPackagedHandlerService()
            .AddHandler("{INST}", "TestApp", "Test_hash", installSource: "Microsoft Store");

        var scanner = new ContextMenuScanner(shellSvc, modernPackagedService: modernSvc);
        var result = scanner.Scan();

        Assert.Equal("Microsoft Store", result[0].PackagedInfo!.InstallSource);
    }
}
