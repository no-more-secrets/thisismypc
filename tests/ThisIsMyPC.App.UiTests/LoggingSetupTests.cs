using System.Text.Json;
using NLog;
using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// The log is where an error gets copied from, so the file must carry the
/// full message, the level, and the exception text in one record.
/// </summary>
public sealed class LoggingSetupTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "tipc-logtest-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AnErrorWithAnExceptionLandsInTheJsonFileWithLevelMessageAndStack()
    {
        var factory = new LogFactory();
        LoggingSetup.Configure(_dataDir, verbose: true, console: false, factory);
        var log = factory.GetLogger("ThisIsMyPC.App.ViewModels.MainWindowViewModel");

        log.Error(new InvalidOperationException("powrprof said no"),
            "Apply {Module}/{Id} failed [{Category}]: {Error}", "power", "active-plan", "AccessDenied", "a Group Policy pins the active power plan");
        factory.Flush();
        factory.Shutdown();

        var file = Directory.GetFiles(Path.Combine(_dataDir, "logs"), "thisismypc-*.log").Single();
        var line = File.ReadAllLines(file).Single(l => l.Contains("active-plan", StringComparison.Ordinal));
        using var record = JsonDocument.Parse(line);
        var root = record.RootElement;

        Assert.Equal("Error", root.GetProperty("@l").GetString());
        Assert.Equal("ThisIsMyPC.App.ViewModels.MainWindowViewModel", root.GetProperty("SourceContext").GetString());
        Assert.Equal("a Group Policy pins the active power plan", root.GetProperty("Error").GetString());
        Assert.Contains("powrprof said no", root.GetProperty("@x").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDebuggerTargetIsQuietWithoutADebugger()
    {
        var factory = new LogFactory();
        var target = new DebuggerLogTarget { Name = "debugger", Layout = "${message}" };
        factory.Setup().LoadConfiguration(b => b.ForLogger().WriteTo(target));

        var exception = Record.Exception(() => factory.GetLogger("t").Info("hello"));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}
