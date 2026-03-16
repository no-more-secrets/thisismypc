using ThisIsMyPC.Interop.Com.Shell;
using ThisIsMyPC.Interop.Win32.Registry;
using ThisIsMyPC.Modules.Shell;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using Xunit.Abstractions;

namespace ThisIsMyPC.Integration.Tests.Shell;

[Trait("Category", "Integration")]
public sealed class ContextMenuOrphanIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ContextMenuOrphanIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Scan_real_system_no_false_orphan_positives()
    {
        // Scan the real system using production services
        var registry = new RegistryService();
        var comService = new ShellExtensionService(registry);
        var scanner = new ContextMenuScanner(comService);

        var handlers = scanner.Scan();

        var orphans = handlers.Where(h => h.IsOrphaned).ToList();
        var nonOrphans = handlers.Where(h => !h.IsOrphaned).ToList();

        _output.WriteLine($"Total COM handlers: {handlers.Count}");
        _output.WriteLine($"Orphaned: {orphans.Count}");
        _output.WriteLine($"Valid: {nonOrphans.Count}");

        // Every non-orphan handler with a DllPath should point to an existing file
        foreach (var h in nonOrphans)
        {
            if (h.DllPath is not null)
            {
                var expanded = Environment.ExpandEnvironmentVariables(h.DllPath);
                if (expanded.Length >= 2 && expanded[0] == '"' && expanded[^1] == '"')
                    expanded = expanded[1..^1];

                Assert.True(File.Exists(expanded),
                    $"Non-orphan handler '{h.Name}' ({h.Clsid}) has DllPath '{h.DllPath}' but file does not exist — false negative in orphan detection");
            }
        }

        // Every orphan should have a valid reason
        foreach (var h in orphans)
        {
            Assert.NotNull(h.OrphanReason);
            Assert.NotEmpty(h.OrphanReason);

            _output.WriteLine($"  Orphan: {h.Name} ({h.Clsid}) — {h.OrphanReason}");

            // If the orphan has a DllPath, the file should genuinely not exist
            if (h.DllPath is not null)
            {
                var expanded = Environment.ExpandEnvironmentVariables(h.DllPath);
                if (expanded.Length >= 2 && expanded[0] == '"' && expanded[^1] == '"')
                    expanded = expanded[1..^1];

                Assert.False(File.Exists(expanded),
                    $"Orphan handler '{h.Name}' ({h.Clsid}) has DllPath '{h.DllPath}' but file EXISTS — false positive in orphan detection");
            }
        }

        // Sanity: scan should find at least some handlers without crashing
        Assert.True(handlers.Count > 0, "Scanner found zero handlers — expected at least Windows built-in handlers");
    }
}
