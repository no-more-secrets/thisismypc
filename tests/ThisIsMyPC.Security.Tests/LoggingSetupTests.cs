using System.Text.Json;
using NLog;
using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.Security.Tests;

/// <summary>
/// The production configuration must actually write a JSON line to the file
/// target under the data directory and route levels as documented. Each test
/// uses its own LogFactory: other test classes in this process log through the
/// global LogManager, and a global configuration here would collect their
/// lines too.
/// </summary>
[Trait("Category", "Security")]
public class LoggingSetupTests
{
    [Fact]
    public void Configure_WritesJsonLineToDailyFileUnderDataDir()
    {
        var dataDir = NewDataDir();
        var factory = new LogFactory();
        try
        {
            var log = LoggingSetup.Configure(dataDir, verbose: false, console: false, factory);
            log.Info("Setup check {Status}", "OK");
            factory.Flush();
            factory.Shutdown();

            var file = Assert.Single(Directory.GetFiles(Path.Combine(dataDir, "logs"), "thisismypc-*.log"));
            Assert.Matches(@"thisismypc-\d{4}-\d{2}-\d{2}\.log$", Path.GetFileName(file));

            var line = Assert.Single(File.ReadAllLines(file));
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("Setup check {Status}", doc.RootElement.GetProperty("@mt").GetString());
            Assert.Equal("OK", doc.RootElement.GetProperty("Status").GetString());
            Assert.Equal(LoggingSetup.AppLoggerName, doc.RootElement.GetProperty("SourceContext").GetString());
        }
        finally
        {
            factory.Shutdown();
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void Configure_ReleaseLevels_DropInteropInfoButKeepInteropWarn()
    {
        var dataDir = NewDataDir();
        var factory = new LogFactory();
        try
        {
            LoggingSetup.Configure(dataDir, verbose: false, console: false, factory);
            var interop = factory.GetLogger("ThisIsMyPC.Interop.Win32.Something");
            var app = factory.GetLogger("ThisIsMyPC.App.Services.Something");

            interop.Info("interop info {Tag}", "drop");
            interop.Warn("interop warn {Tag}", "keep");
            app.Debug("app debug {Tag}", "drop");
            app.Info("app info {Tag}", "keep");
            factory.Flush();
            factory.Shutdown();

            var file = Assert.Single(Directory.GetFiles(Path.Combine(dataDir, "logs"), "thisismypc-*.log"));
            var templates = File.ReadAllLines(file)
                .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("@mt").GetString() ?? "")
                .ToArray();

            Assert.Equal(["interop warn {Tag}", "app info {Tag}"], templates);
        }
        finally
        {
            factory.Shutdown();
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void Configure_VerboseLevels_KeepEverything()
    {
        var dataDir = NewDataDir();
        var factory = new LogFactory();
        try
        {
            LoggingSetup.Configure(dataDir, verbose: true, console: false, factory);
            var interop = factory.GetLogger("ThisIsMyPC.Interop.Win32.Something");

            interop.Trace("interop trace {Tag}", "keep");
            factory.Flush();
            factory.Shutdown();

            var file = Assert.Single(Directory.GetFiles(Path.Combine(dataDir, "logs"), "thisismypc-*.log"));
            var line = Assert.Single(File.ReadAllLines(file));
            Assert.Contains("interop trace {Tag}", line, StringComparison.Ordinal);
        }
        finally
        {
            factory.Shutdown();
            Directory.Delete(dataDir, recursive: true);
        }
    }

    private static string NewDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tipc-logsetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
