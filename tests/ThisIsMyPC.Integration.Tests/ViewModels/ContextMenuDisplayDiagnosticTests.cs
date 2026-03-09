using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

[Trait("Category", "Diagnostic")]
public sealed class ContextMenuDisplayDiagnosticTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly RegistryService _registry = new();

    public ContextMenuDisplayDiagnosticTests(ITestOutputHelper output) => _output = output;

    public void Dispose() { }

    [Fact]
    public void Display_all_context_menu_entries()
    {
        // Scan real system
        var staticVerbService = new StaticVerbService(_registry, ShellRegistryPaths.StaticVerbScopePaths);
        var comService = new ShellExtensionService(_registry);
        var scanner = new ContextMenuScanner(comService, staticVerbService: staticVerbService);
        var handlers = scanner.Scan();

        // Build VMs exactly as the app does
        var pending = new FakePendingChangesService();
        using var vm = new ContextMenuViewModel(handlers, pending, _registry);

        // Dump each tab
        DumpTab("FILE", vm.FileHandlers);
        DumpTab("FOLDER", vm.FolderHandlers);
        DumpTab("FOLDER BACKGROUND", vm.FolderBackgroundHandlers);
        DumpTab("DESKTOP", vm.DesktopHandlers);
        DumpTab("MISC", vm.MiscHandlers);

        // Registry view mode
        _output.WriteLine("========================================");
        _output.WriteLine("=== REGISTRY VIEW MODE (first 5) ===");
        _output.WriteLine("========================================");
        vm.IsRegistryViewMode = true;
        var sample = GetUniqueVms(vm).Take(5);
        foreach (var h in sample)
        {
            _output.WriteLine($"  [{h.HandlerTypeBadge}] {h.Label}");
            _output.WriteLine($"    Description: {h.Description}");
            _output.WriteLine($"    SystemPath:  {h.SystemPath}");
            _output.WriteLine("");
        }
        vm.IsRegistryViewMode = false;

        // Summary
        var unique = GetUniqueVms(vm).ToList();
        var comCount = unique.Count(h => h.HandlerType == HandlerType.ComHandler);
        var verbCount = unique.Count(h => h.HandlerType == HandlerType.StaticVerb);
        _output.WriteLine("=== SUMMARY ===");
        _output.WriteLine($"Total unique VMs: {unique.Count} (COM: {comCount}, Static Verb: {verbCount})");
        _output.WriteLine($"Tab counts: {vm.FileHandlerCount}, {vm.FolderHandlerCount}, {vm.FolderBackgroundHandlerCount}, {vm.DesktopHandlerCount}, {vm.MiscHandlerCount}");
    }

    private void DumpTab(string tabName, IReadOnlyList<ContextMenuHandlerViewModel> handlers)
    {
        _output.WriteLine($"========================================");
        _output.WriteLine($"=== {tabName} TAB ({handlers.Count} entries) ===");
        _output.WriteLine($"========================================");

        foreach (var h in handlers)
        {
            var enabledMark = h.IsEnabled ? "ON " : "OFF";
            _output.WriteLine($"  [{enabledMark}] [{h.HandlerTypeBadge}] {h.Label}");
            _output.WriteLine($"       Description:  {h.Description}");
            if (!string.IsNullOrEmpty(h.WarningText))
                _output.WriteLine($"       Warning:      {h.WarningText}");
            if (!string.IsNullOrEmpty(h.DisableMethodText))
                _output.WriteLine($"       DisableMethod:{h.DisableMethodText}");
            if (!string.IsNullOrEmpty(h.ScopeNote))
                _output.WriteLine($"       ScopeNote:    {h.ScopeNote}");
            if (h.HandlerType == HandlerType.StaticVerb && h.VerbInfo is { } vi)
            {
                var extras = new List<string>();
                if (vi.IsExtended) extras.Add("Shift-only");
                if (vi.HasLuaShield) extras.Add("UAC");
                if (vi.IsProgrammaticAccessOnly) extras.Add("Script-only");
                if (vi.Position is not null) extras.Add($"Pos:{vi.Position}");
                if (vi.DelegateExecuteClsid is not null) extras.Add($"DE:{vi.DelegateExecuteClsid}");
                if (extras.Count > 0)
                    _output.WriteLine($"       VerbFlags:    {string.Join(", ", extras)}");
            }
            _output.WriteLine("");
        }
    }

    private static IEnumerable<ContextMenuHandlerViewModel> GetUniqueVms(ContextMenuViewModel vm)
    {
        var seen = new HashSet<ContextMenuHandlerViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var collection in new[] { vm.FileHandlers, vm.FolderHandlers, vm.FolderBackgroundHandlers, vm.DesktopHandlers, vm.MiscHandlers })
            foreach (var h in collection)
                if (seen.Add(h))
                    yield return h;
    }
}
