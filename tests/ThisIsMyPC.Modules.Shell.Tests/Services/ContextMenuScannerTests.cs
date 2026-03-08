using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class ContextMenuScannerTests
{
    private sealed class FakeShellExtensionService : IShellExtensionService
    {
        private readonly List<ShellExtensionInfo> _handlers = [];

        public void AddHandler(ShellExtensionInfo handler) => _handlers.Add(handler);

        public OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers()
            => OperationResult<IReadOnlyList<ShellExtensionInfo>>.Success(_handlers);
    }

    [Fact]
    public void Scan_deduplicates_handlers_by_CLSID()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\*\shellex\ContextMenuHandlers\7-Zip", "All files",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\Directory\shellex\ContextMenuHandlers\7-Zip", "Directories",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\Folder\shellex\ContextMenuHandlers\7-Zip", "Folders",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Single(result);
        Assert.Equal("{23170F69-40C1-278A-1000-000100020000}", result[0].Clsid);
    }

    [Fact]
    public void Scan_merges_registry_paths_for_deduped_handler()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\*\shellex\ContextMenuHandlers\7-Zip", "All files",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\Directory\shellex\ContextMenuHandlers\7-Zip", "Directories",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Equal(2, result[0].AllRegistryPaths!.Count);
        Assert.Contains(@"HKCR\*\shellex\ContextMenuHandlers\7-Zip", result[0].AllRegistryPaths!);
        Assert.Contains(@"HKCR\Directory\shellex\ContextMenuHandlers\7-Zip", result[0].AllRegistryPaths!);
    }

    [Fact]
    public void Scan_merges_scopes_for_deduped_handler()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\*\shellex\ContextMenuHandlers\7-Zip", "All files",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\Directory\shellex\ContextMenuHandlers\7-Zip", "Directories",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Equal(2, result[0].AllScopes!.Count);
        Assert.Contains("All files", result[0].AllScopes!);
        Assert.Contains("Directories", result[0].AllScopes!);
    }

    [Fact]
    public void Scan_IsEnabled_false_when_any_registration_is_disabled()
    {
        var fake = new FakeShellExtensionService();
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\*\shellex\ContextMenuHandlers\7-Zip", "All files",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));
        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\Directory\shellex\ContextMenuHandlers\7-Zip", "Directories",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", false));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.False(result[0].IsEnabled);
    }

    [Fact]
    public void Scan_assigns_classification()
    {
        var fake = new FakeShellExtensionService();
        // Critical CLSID (Open With)
        fake.AddHandler(new ShellExtensionInfo("Open With", "{09799AFB-AD67-11d1-ABCD-00C04FC30936}",
            @"HKCR\*\shellex\ContextMenuHandlers\OpenWith", "All files",
            @"C:\Windows\System32\shell32.dll", "Microsoft Corporation", true));

        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Equal(HandlerClassification.Critical, result[0].Classification);
    }

    [Fact]
    public void Scan_returns_empty_list_on_failure()
    {
        var fake = new FailingShellExtensionService();
        var scanner = new ContextMenuScanner(fake);
        var result = scanner.Scan();

        Assert.Empty(result);
    }

    private sealed class FailingShellExtensionService : IShellExtensionService
    {
        public OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers()
            => OperationResult<IReadOnlyList<ShellExtensionInfo>>.Failure("Failed", ErrorCategory.ServiceUnavailable);
    }
}
