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

    /// <summary>
    /// Reads the REAL registry on this PC, runs the full scanner + ViewModel pipeline,
    /// and dumps exactly what the app would show in each tab. Use this to verify
    /// display names, tab assignments, toggle state, and scope badges before shipping.
    /// Run: dotnet test --filter "Display_full_system_state" -v detailed
    /// </summary>
    [Fact]
    public void Display_full_system_state()
    {
        // === Scan real system ===
        var staticVerbService = new StaticVerbService(_registry, ShellRegistryPaths.StaticVerbScopePaths);
        var comService = new ShellExtensionService(_registry);
        var scanner = new ContextMenuScanner(comService, staticVerbService: staticVerbService);
        var handlers = scanner.Scan();

        // === Build VMs exactly as the app does ===
        var pending = new FakePendingChangesService();
        using var vm = new ContextMenuViewModel(handlers, pending, _registry);

        // === Dump each tab ===
        DumpTab("FILE", vm.FileHandlers);
        DumpTab("FOLDER", vm.FolderHandlers);
        DumpTab("FOLDER BACKGROUND", vm.FolderBackgroundHandlers);
        DumpTab("DESKTOP", vm.DesktopHandlers);
        DumpTab("MULTI", vm.MultiHandlers);
        DumpTab("MISC", vm.MiscHandlers);

        // === Summary ===
        _output.WriteLine("============================================================");
        _output.WriteLine("=== SUMMARY ===");
        _output.WriteLine("============================================================");
        var unique = GetUniqueVms(vm).ToList();
        _output.WriteLine($"Tabs: {vm.FileHandlerCount}, {vm.FolderHandlerCount}, {vm.FolderBackgroundHandlerCount}, {vm.DesktopHandlerCount}, {vm.MultiHandlerCount}, {vm.MiscHandlerCount}");
        _output.WriteLine($"Unique handlers: {unique.Count}");
        _output.WriteLine($"  COM handlers:     {unique.Count(h => h.HandlerType == HandlerType.ComHandler)}");
        _output.WriteLine($"  Static verbs:     {unique.Count(h => h.HandlerType == HandlerType.StaticVerb)}");
        _output.WriteLine($"  Modern packaged:  {unique.Count(h => h.HandlerType == HandlerType.ModernPackaged)}");
        _output.WriteLine($"  Drag-drop:        {unique.Count(h => h.HandlerType == HandlerType.DragDropHandler)}");
        _output.WriteLine($"  Orphaned:         {unique.Count(h => h.IsOrphaned)}");
        _output.WriteLine($"  Dual-registered:  {unique.Count(h => h.IsDualRegistered)}");
        _output.WriteLine("");

        // === Toggleable vs non-toggleable breakdown ===
        var toggleable = unique.Where(h => h.IsToggleEnabled).ToList();
        var nonToggleable = unique.Where(h => !h.IsToggleEnabled).ToList();
        _output.WriteLine($"Toggleable: {toggleable.Count}");
        foreach (var h in toggleable)
            _output.WriteLine($"  [{h.HandlerTypeBadge}] {h.Label} -- {h.Clsid}");

        _output.WriteLine("");
        _output.WriteLine($"Non-toggleable: {nonToggleable.Count}");
        foreach (var h in nonToggleable)
            _output.WriteLine($"  [{h.HandlerTypeBadge}] {h.Label} -- {h.ToggleDisabledTooltip ?? h.Description}");

        // === Classic context menu state ===
        _output.WriteLine("");
        _output.WriteLine($"Classic context menu shim active: {vm.IsClassicMenuActive}");
    }

    private void DumpTab(string tabName, IReadOnlyList<ContextMenuHandlerViewModel> handlers)
    {
        _output.WriteLine($"============================================================");
        _output.WriteLine($"=== {tabName} TAB ({handlers.Count} entries) ===");
        _output.WriteLine($"============================================================");

        if (handlers.Count == 0)
        {
            _output.WriteLine("  (empty)");
            _output.WriteLine("");
            return;
        }

        foreach (var h in handlers)
        {
            var toggle = h.IsToggleEnabled ? (h.IsEnabled ? "ON " : "OFF") : "---";
            var inactive = h.IsInactive ? " [INACTIVE]" : "";
            _output.WriteLine($"  [{toggle}] [{h.HandlerTypeBadge,-16}] {h.Label}{inactive}");
            _output.WriteLine($"         Description:  {h.Description}");
            _output.WriteLine($"         CLSID:        {h.Clsid}");
            _output.WriteLine($"         Scopes:       {string.Join(", ", h.AllScopes)}");
            _output.WriteLine($"         Paths:        {string.Join(", ", h.AllRegistryPaths)}");

            if (h.DllPath is not null)
                _output.WriteLine($"         DLL:          {h.DllPath}");

            if (!string.IsNullOrEmpty(h.WarningText))
                _output.WriteLine($"         Warning:      {h.WarningText}");

            if (!string.IsNullOrEmpty(h.DisableMethodText))
                _output.WriteLine($"         DisableMethod: {h.DisableMethodText}");

            if (!h.IsToggleEnabled && h.ToggleDisabledTooltip is not null)
                _output.WriteLine($"         WhyNoToggle:  {h.ToggleDisabledTooltip}");

            // Scope badges (Multi tab)
            if (h.ScopeBadges.Count > 0)
                _output.WriteLine($"         ScopeBadges:  {string.Join(", ", h.ScopeBadges.Select(b => b.Label))}");

            // Static verb details
            if (h.HandlerType == HandlerType.StaticVerb && h.VerbInfo is { } vi)
            {
                var flags = new List<string>();
                if (vi.IsExtended) flags.Add("Shift-only");
                if (vi.HasLuaShield) flags.Add("UAC");
                if (vi.IsProgrammaticAccessOnly) flags.Add("Script-only");
                if (vi.Position is not null) flags.Add($"Pos:{vi.Position}");
                if (vi.CommandLine is not null) flags.Add($"Cmd:{vi.CommandLine}");
                if (vi.DelegateExecuteClsid is not null) flags.Add($"DelegateExec:{vi.DelegateExecuteClsid}");
                if (flags.Count > 0)
                    _output.WriteLine($"         VerbInfo:     {string.Join(" | ", flags)}");
            }

            if (h.IsOrphaned)
                _output.WriteLine($"         Orphan:       {h.OrphanReason}");
            if (h.IsInactive)
                _output.WriteLine($"         InactiveWhy:  {h.InactiveReason}");

            _output.WriteLine("");
        }
    }

    private static IEnumerable<ContextMenuHandlerViewModel> GetUniqueVms(ContextMenuViewModel vm)
    {
        var seen = new HashSet<ContextMenuHandlerViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var collection in new[]
                 {
                     vm.FileHandlers, vm.FolderHandlers, vm.FolderBackgroundHandlers,
                     vm.DesktopHandlers, vm.MultiHandlers, vm.MiscHandlers,
                 })
        {
            foreach (var h in collection)
                if (seen.Add(h))
                    yield return h;
        }
    }
}
