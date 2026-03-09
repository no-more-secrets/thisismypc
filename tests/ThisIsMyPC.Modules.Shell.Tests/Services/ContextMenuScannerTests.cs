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

    private sealed class FakeContextMenuProbe : IContextMenuProbe
    {
        private readonly Dictionary<(string Clsid, ContextMenuSurface Surface), bool> _results = [];

        public void SetResult(string clsid, ContextMenuSurface surface, bool appears)
            => _results[(clsid, surface)] = appears;

        public OperationResult<bool> HandlerAppearsOnSurface(string clsid, ContextMenuSurface surface)
        {
            // Case-insensitive CLSID lookup
            foreach (var (key, value) in _results)
            {
                if (string.Equals(key.Clsid, clsid, StringComparison.OrdinalIgnoreCase) && key.Surface == surface)
                    return OperationResult<bool>.Success(value);
            }
            return OperationResult<bool>.Success(true); // default: appears
        }
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

    [Fact]
    public void Scan_probes_folder_background_handlers_for_surface_visibility()
    {
        var fake = new FakeShellExtensionService();
        var probe = new FakeContextMenuProbe();

        // Handler appears on folder background but NOT desktop
        var clsid = "{0440049F-D1DC-4E46-B27B-98393D79486B}";
        fake.AddHandler(new ShellExtensionInfo("PowerRenameExt", clsid,
            @"HKCR\Directory\Background\shellex\ContextMenuHandlers\PowerRenameExt", "Folder background",
            @"C:\PowerToys\PowerRenameExt.dll", "Microsoft Corporation", true));
        probe.SetResult(clsid, ContextMenuSurface.FolderBackground, true);
        probe.SetResult(clsid, ContextMenuSurface.DesktopBackground, false);

        var scanner = new ContextMenuScanner(fake, probe);
        var result = scanner.Scan();

        Assert.NotNull(result[0].VisibleSurfaces);
        Assert.Contains(ContextMenuSurface.FolderBackground, result[0].VisibleSurfaces!);
        Assert.DoesNotContain(ContextMenuSurface.DesktopBackground, result[0].VisibleSurfaces!);
    }

    [Fact]
    public void Scan_probes_desktop_only_handler()
    {
        var fake = new FakeShellExtensionService();
        var probe = new FakeContextMenuProbe();

        // NVIDIA handler appears on desktop but NOT folder background
        var clsid = "{F2E8B4A1-9C7D-4F6E-B3A5-8D2C1F4E9B7A}";
        fake.AddHandler(new ShellExtensionInfo("NvAppDesktopContext", clsid,
            @"HKCR\Directory\Background\shellex\ContextMenuHandlers\NvAppDesktopContext", "Folder background",
            @"C:\NVIDIA\nvcpl.dll", "NVIDIA Corporation", true));
        probe.SetResult(clsid, ContextMenuSurface.FolderBackground, false);
        probe.SetResult(clsid, ContextMenuSurface.DesktopBackground, true);

        var scanner = new ContextMenuScanner(fake, probe);
        var result = scanner.Scan();

        Assert.NotNull(result[0].VisibleSurfaces);
        Assert.DoesNotContain(ContextMenuSurface.FolderBackground, result[0].VisibleSurfaces!);
        Assert.Contains(ContextMenuSurface.DesktopBackground, result[0].VisibleSurfaces!);
    }

    [Fact]
    public void Scan_no_probe_for_non_background_handlers()
    {
        var fake = new FakeShellExtensionService();
        var probe = new FakeContextMenuProbe();

        fake.AddHandler(new ShellExtensionInfo("7-Zip", "{23170F69-40C1-278A-1000-000100020000}",
            @"HKCR\*\shellex\ContextMenuHandlers\7-Zip", "All files",
            @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov", true));

        var scanner = new ContextMenuScanner(fake, probe);
        var result = scanner.Scan();

        Assert.Null(result[0].VisibleSurfaces);
    }

    [Fact]
    public void Scan_no_probe_when_probe_is_null()
    {
        var fake = new FakeShellExtensionService();

        fake.AddHandler(new ShellExtensionInfo("PowerRenameExt", "{0440049F-D1DC-4E46-B27B-98393D79486B}",
            @"HKCR\Directory\Background\shellex\ContextMenuHandlers\PowerRenameExt", "Folder background",
            @"C:\PowerToys\PowerRenameExt.dll", "Microsoft Corporation", true));

        var scanner = new ContextMenuScanner(fake, contextMenuProbe: null);
        var result = scanner.Scan();

        Assert.Null(result[0].VisibleSurfaces);
    }

    [Fact]
    public void Scan_both_surfaces_when_handler_appears_on_both()
    {
        var fake = new FakeShellExtensionService();
        var probe = new FakeContextMenuProbe();

        var clsid = "{D969A300-E7FF-11d0-A93B-00A0C90F2719}";
        fake.AddHandler(new ShellExtensionInfo("New", clsid,
            @"HKCR\Directory\Background\shellex\ContextMenuHandlers\New", "Folder background",
            @"C:\Windows\System32\shell32.dll", "Microsoft Corporation", true));
        probe.SetResult(clsid, ContextMenuSurface.FolderBackground, true);
        probe.SetResult(clsid, ContextMenuSurface.DesktopBackground, true);

        var scanner = new ContextMenuScanner(fake, probe);
        var result = scanner.Scan();

        Assert.NotNull(result[0].VisibleSurfaces);
        Assert.Contains(ContextMenuSurface.FolderBackground, result[0].VisibleSurfaces!);
        Assert.Contains(ContextMenuSurface.DesktopBackground, result[0].VisibleSurfaces!);
    }

    private sealed class FailingShellExtensionService : IShellExtensionService
    {
        public OperationResult<IReadOnlyList<ShellExtensionInfo>> EnumerateContextMenuHandlers()
            => OperationResult<IReadOnlyList<ShellExtensionInfo>>.Failure("Failed", ErrorCategory.ServiceUnavailable);
    }
}
