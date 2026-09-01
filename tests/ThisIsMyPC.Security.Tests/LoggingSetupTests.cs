using System.Text.Json;
using NLog;
using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.Security.Tests;

/// <summary>
/// The production configuration must actually write a JSON line to the file
/// target under the data directory. Uses the global LogManager, so this is
/// the only test class that does; it shuts the configuration down after.
/// </summary>
[Trait("Category", "Security")]
public class LoggingSetupTests
{
    [Fact]
    public void Configure_WritesJsonLineToDailyFileUnderDataDir()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "tipc-logsetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        try
        {
            var log = LoggingSetup.Configure(dataDir, verbose: false, console: false);
            log.Info("Setup check {Status}", "OK");
            LogManager.Flush();

            var files = Directory.GetFiles(Path.Combine(dataDir, "logs"), "thisismypc-*.log");
            var file = Assert.Single(files);
            Assert.Matches(@"thisismypc-\d{4}-\d{2}-\d{2}\.log$", Path.GetFileName(file));

            LogManager.Shutdown();

            var line = Assert.Single(File.ReadAllLines(file));
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("Setup check {Status}", doc.RootElement.GetProperty("@mt").GetString());
            Assert.Equal("OK", doc.RootElement.GetProperty("Status").GetString());
            Assert.Equal(LoggingSetup.AppLoggerName, doc.RootElement.GetProperty("SourceContext").GetString());
        }
        finally
        {
            LogManager.Shutdown();
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Fact]
    public void Configure_ReleaseLevels_DropInteropInfoButKeepInteropWarn()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "tipc-logsetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        try
        {
            LoggingSetup.Configure(dataDir, verbose: false, console: false);
            var interop = LogManager.GetLogger("ThisIsMyPC.Interop.Win32.Something");
            var app = LogManager.GetLogger("ThisIsMyPC.App.Services.Something");

            interop.Info("interop info {Tag}", "drop");
            interop.Warn("interop warn {Tag}", "keep");
            app.Debug("app debug {Tag}", "drop");
            app.Info("app info {Tag}", "keep");
            LogManager.Flush();
            LogManager.Shutdown();

            var file = Assert.Single(Directory.GetFiles(Path.Combine(dataDir, "logs"), "thisismypc-*.log"));
            var templates = File.ReadAllLines(file)
                .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("@mt").GetString() ?? "")
                .ToArray();

            Assert.Equal(["interop warn {Tag}", "app info {Tag}"], templates);
        }
        finally
        {
            LogManager.Shutdown();
            Directory.Delete(dataDir, recursive: true);
        }
    }
}
