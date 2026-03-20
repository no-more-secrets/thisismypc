using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests.Registry;

[Trait("Category", "Integration")]
public sealed class StaticVerbIntegrationTests : IDisposable
{
    private const string SandboxKeyPath = @"HKCU\Software\ThisIsMyPC\Tests\StaticVerbs";
    private readonly RegistryService _registry = new();
    private readonly ITestOutputHelper _output;

    public StaticVerbIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _registry.WriteDWord(SandboxKeyPath, "setup", 1);
    }

    public void Dispose()
    {
        _registry.DeleteKey(SandboxKeyPath, recursive: true);
        _registry.DeleteKey(@"HKCU\Software\ThisIsMyPC\Tests", recursive: false);
        _registry.DeleteKey(@"HKCU\Software\ThisIsMyPC", recursive: false);
    }

    // --- Diagnostic: scan real HKCR static verbs ---

    [Fact]
    public void Diagnostic_scan_real_registry_static_verbs()
    {
        var service = new StaticVerbService(_registry, ShellRegistryPaths.StaticVerbScopePaths);
        var result = service.EnumerateStaticVerbs();

        Assert.True(result.IsSuccess, "Static verb enumeration failed");
        var verbs = result.Value!;

        _output.WriteLine($"=== Static Verb Scan Results ({verbs.Count} total) ===");
        _output.WriteLine("");

        // Group by scope for readability
        var grouped = verbs.GroupBy(v => v.Scope).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            _output.WriteLine($"--- {group.Key} ({group.Count()} verbs) ---");
            foreach (var verb in group.OrderBy(v => v.VerbName))
            {
                var flags = new List<string>();
                if (verb.IsExtended) flags.Add("Shift-only");
                if (verb.IsLegacyDisabled) flags.Add("DISABLED");
                if (verb.IsProgrammaticAccessOnly) flags.Add("Script-only");
                if (verb.HasLuaShield) flags.Add("UAC");
                if (verb.HasDropTarget) flags.Add("DropTarget");
                if (verb.DelegateExecuteClsid is not null) flags.Add($"DE:{verb.DelegateExecuteClsid}");
                if (verb.Position is not null) flags.Add($"Pos:{verb.Position}");

                var exec = verb.CommandLine ?? verb.DelegateExecuteClsid ?? "(no exec)";
                var flagText = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
                var display = verb.MuiVerb is not null ? $"{verb.VerbName} ({verb.MuiVerb})" : verb.VerbName;

                _output.WriteLine($"  {display}{flagText}");
                _output.WriteLine($"    Path: {verb.RegistryPath}");
                if (verb.CommandLine is not null)
                    _output.WriteLine($"    Cmd:  {verb.CommandLine}");
            }
            _output.WriteLine("");
        }

        // Summary stats
        var deCount = verbs.Count(v => v.DelegateExecuteClsid is not null);
        var cmdCount = verbs.Count(v => v.CommandLine is not null);
        var bothCount = verbs.Count(v => v.CommandLine is not null && v.DelegateExecuteClsid is not null);
        var dropCount = verbs.Count(v => v.HasDropTarget);
        var noExecCount = verbs.Count(v => v.CommandLine is null && v.DelegateExecuteClsid is null && !v.HasDropTarget);
        var disabledCount = verbs.Count(v => v.IsLegacyDisabled);
        var extendedCount = verbs.Count(v => v.IsExtended);

        _output.WriteLine("=== Summary ===");
        _output.WriteLine($"Total verbs:      {verbs.Count}");
        _output.WriteLine($"Command-only:     {cmdCount - bothCount}");
        _output.WriteLine($"DelegateExec-only: {deCount - bothCount}");
        _output.WriteLine($"Both cmd+DE:      {bothCount}");
        _output.WriteLine($"DropTarget:       {dropCount}");
        _output.WriteLine($"No execution:     {noExecCount}");
        _output.WriteLine($"LegacyDisabled:   {disabledCount}");
        _output.WriteLine($"Extended (Shift): {extendedCount}");

        // Basic sanity: V4 audit found 78 verbs, we should find a similar number
        Assert.True(verbs.Count > 20, $"Expected at least 20 verbs, got {verbs.Count}");
    }

    [Fact]
    public void Diagnostic_scan_deduplication_report()
    {
        var service = new StaticVerbService(_registry, ShellRegistryPaths.StaticVerbScopePaths);
        var result = service.EnumerateStaticVerbs();
        Assert.True(result.IsSuccess);
        var verbs = result.Value!;

        // Find multi-scope verbs (dedup candidates)
        var grouped = verbs
            .GroupBy(v => $"{v.VerbName}|{v.CommandLine ?? v.DelegateExecuteClsid ?? "no-exec"}",
                StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .ToList();

        _output.WriteLine("=== Multi-Scope Verbs (Deduplication Candidates) ===");
        foreach (var group in grouped)
        {
            var first = group.First();
            _output.WriteLine($"  {first.VerbName} ({group.Count()} registrations):");
            foreach (var v in group)
                _output.WriteLine($"    {v.Scope} -> {v.RegistryPath}");
        }

        if (!grouped.Any())
            _output.WriteLine("  (none found)");
    }

    [Fact]
    public void Diagnostic_scanner_unified_output()
    {
        // Test the full ContextMenuScanner pipeline with static verbs
        var service = new StaticVerbService(_registry, ShellRegistryPaths.StaticVerbScopePaths);
        var fakeComService = new EmptyShellExtensionService();
        var scanner = new ContextMenuScanner(fakeComService, staticVerbService: service);

        var handlers = scanner.Scan();
        var staticVerbs = handlers.Where(h => h.HandlerType == HandlerType.StaticVerb).ToList();

        _output.WriteLine($"=== Scanner Output: {staticVerbs.Count} deduplicated static verbs ===");
        _output.WriteLine("");

        foreach (var handler in staticVerbs.OrderBy(h => h.AppliesTo).ThenBy(h => h.Name))
        {
            var verb = handler.VerbInfo!;
            var classification = handler.Classification;
            var pathCount = handler.AllRegistryPaths?.Count ?? 1;
            var scopeCount = handler.AllScopes?.Count ?? 1;

            _output.WriteLine($"  [{classification}] {handler.Name}");
            _output.WriteLine($"    Scopes: {string.Join(", ", handler.AllScopes ?? [handler.AppliesTo])} ({pathCount} paths)");
            _output.WriteLine($"    Enabled: {handler.IsEnabled} | Extended: {verb.IsExtended} | LuaShield: {verb.HasLuaShield}");
            if (verb.CommandLine is not null)
                _output.WriteLine($"    Cmd: {verb.CommandLine}");
            if (verb.DelegateExecuteClsid is not null)
                _output.WriteLine($"    DE: {verb.DelegateExecuteClsid}");
        }
    }

    // --- Sandbox: LegacyDisable toggle cycle ---

    [Fact]
    public void Sandbox_toggle_LegacyDisable_write_and_delete()
    {
        // Create a fake verb in the sandbox
        var shellPath = $@"{SandboxKeyPath}\shell";
        var verbKeyPath = $@"{shellPath}\testverb";
        var commandKeyPath = $@"{verbKeyPath}\command";

        _registry.WriteString(verbKeyPath, "MUIVerb", "Test Verb");
        _registry.WriteString(commandKeyPath, "", "notepad.exe");

        // Scan with sandbox scope
        var sandboxScopes = new List<(string KeyPath, string Scope)>
        {
            (shellPath, "Sandbox")
        };
        var service = new StaticVerbService(_registry, sandboxScopes);
        var result = service.EnumerateStaticVerbs();

        Assert.True(result.IsSuccess);
        var verb = Assert.Single(result.Value!);
        Assert.Equal("testverb", verb.VerbName);
        Assert.False(verb.IsLegacyDisabled);

        // Disable: write LegacyDisable empty string
        var writeResult = _registry.WriteString(verbKeyPath, "LegacyDisable", "");
        Assert.True(writeResult.IsSuccess);

        // Re-scan: should now be disabled
        result = service.EnumerateStaticVerbs();
        verb = Assert.Single(result.Value!);
        Assert.True(verb.IsLegacyDisabled);

        // Enable: delete LegacyDisable
        var deleteResult = _registry.DeleteValue(verbKeyPath, "LegacyDisable");
        Assert.True(deleteResult.IsSuccess);

        // Re-scan: should be enabled again
        result = service.EnumerateStaticVerbs();
        verb = Assert.Single(result.Value!);
        Assert.False(verb.IsLegacyDisabled);

        _output.WriteLine("Sandbox toggle cycle: PASS (write -> scan disabled -> delete -> scan enabled)");
    }

    [Fact]
    public void Sandbox_scan_reads_all_metadata()
    {
        var shellPath = $@"{SandboxKeyPath}\shell";
        var verbKeyPath = $@"{shellPath}\myverb";
        var commandKeyPath = $@"{verbKeyPath}\command";

        _registry.WriteString(verbKeyPath, "MUIVerb", "My Custom Verb");
        _registry.WriteString(verbKeyPath, "Icon", "shell32.dll,1");
        _registry.WriteString(verbKeyPath, "Position", "Top");
        _registry.WriteString(verbKeyPath, "Extended", "");
        _registry.WriteString(verbKeyPath, "HasLUAShield", "");
        _registry.WriteString(commandKeyPath, "", @"C:\test\app.exe ""%1""");
        _registry.WriteString(commandKeyPath, "DelegateExecute", "{12345678-abcd-ef01-2345-67890abcdef0}");

        var sandboxScopes = new List<(string KeyPath, string Scope)> { (shellPath, "Sandbox") };
        var service = new StaticVerbService(_registry, sandboxScopes);
        var result = service.EnumerateStaticVerbs();

        var verb = Assert.Single(result.Value!);
        Assert.Equal("myverb", verb.VerbName);
        Assert.Equal("My Custom Verb", verb.MuiVerb);
        Assert.Equal("shell32.dll,1", verb.Icon);
        Assert.Equal("Top", verb.Position);
        Assert.True(verb.IsExtended);
        Assert.True(verb.HasLuaShield);
        Assert.Equal(@"C:\test\app.exe ""%1""", verb.CommandLine);
        Assert.Equal("{12345678-abcd-ef01-2345-67890abcdef0}", verb.DelegateExecuteClsid);

        _output.WriteLine("Sandbox metadata read: PASS (all fields verified)");
    }

    // Minimal IShellExtensionService for scanner tests (no COM handlers)
    private sealed class EmptyShellExtensionService : Interop.Com.Shell.IShellExtensionService
    {
        public Core.Results.OperationResult<IReadOnlyList<Interop.Com.Shell.ShellExtensionInfo>> EnumerateContextMenuHandlers()
            => Core.Results.OperationResult<IReadOnlyList<Interop.Com.Shell.ShellExtensionInfo>>.Success([]);

        public bool IsBlockedByCLSID(string clsid) => false;
        public IReadOnlySet<string> GetBlockedClsids() => new HashSet<string>();

        public Core.Results.OperationResult<IReadOnlyList<Interop.Com.Shell.DragDropHandlerInfo>> EnumerateDragDropHandlers()
            => Core.Results.OperationResult<IReadOnlyList<Interop.Com.Shell.DragDropHandlerInfo>>.Success([]);
    }
}
