using System.Text.Json;
using NLog;
using NLog.Config;
using NLog.Targets;
using ThisIsMyPC.App.Services;

namespace ThisIsMyPC.Security.Tests;

/// <summary>
/// The production JSON layout must keep one event per line with every value
/// JSON-escaped, so a log reader cannot be fed forged lines through user
/// controlled strings (CWE-117).
/// </summary>
[Trait("Category", "Security")]
public class LoggingSecurityTests
{
    [Fact]
    public void JsonLayout_ProducesValidJsonWithEnvelope()
    {
        var (logger, target) = CreateJsonLogger();
        logger.Info("Test {Value}", "hello");

        var output = Single(target);
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("@t", out _), "envelope missing @t (timestamp)");
        Assert.Equal("Test {Value}", root.GetProperty("@mt").GetString());
        Assert.Equal("hello", root.GetProperty("Value").GetString());
    }

    [Fact]
    public void JsonLayout_CrlfInPropertyValue_ProducesSingleLine_CWE117()
    {
        var (logger, target) = CreateJsonLogger();
        logger.Info("User action: {Input}", "legit\r\n[WARN] Fake security event");

        var output = Single(target);
        Assert.DoesNotContain('\n', output);
        Assert.DoesNotContain('\r', output);

        using var doc = JsonDocument.Parse(output);
        var input = doc.RootElement.GetProperty("Input").GetString();
        Assert.Contains("\r\n", input, StringComparison.Ordinal);
        Assert.Contains("[WARN] Fake security event", input, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonLayout_SpecialCharactersInValues_JsonEscaped()
    {
        var (logger, target) = CreateJsonLogger();
        logger.Info("Path: {FilePath}", "C:\\Users\\test\\file \"quoted\".txt");

        using var doc = JsonDocument.Parse(Single(target));
        var path = doc.RootElement.GetProperty("FilePath").GetString();
        Assert.Equal("C:\\Users\\test\\file \"quoted\".txt", path);
    }

    [Fact]
    public void JsonLayout_OutputIsJsonObject_NotPlainText()
    {
        var (logger, target) = CreateJsonLogger();
        logger.Info("Startup check {Status}", "OK");

        var output = Single(target);
        Assert.StartsWith("{", output, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(output);
        Assert.Equal("OK", doc.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public void JsonLayout_ExceptionLandsInEnvelope_NotAsExtraLines()
    {
        var (logger, target) = CreateJsonLogger();
        logger.Error(new InvalidOperationException("boom"), "Failed {Step}", "apply");

        var output = Single(target);
        using var doc = JsonDocument.Parse(output);
        Assert.Contains("boom", doc.RootElement.GetProperty("@x").GetString(), StringComparison.Ordinal);
        Assert.Equal("apply", doc.RootElement.GetProperty("Step").GetString());
    }

    private static string Single(MemoryTarget target)
    {
        var line = Assert.Single(target.Logs);
        Assert.False(string.IsNullOrWhiteSpace(line));
        return line;
    }

    /// <summary>A private LogFactory so the test never touches the global LogManager.</summary>
    private static (Logger logger, MemoryTarget target) CreateJsonLogger()
    {
        var target = new MemoryTarget("memory") { Layout = LoggingSetup.CreateJsonLayout() };
        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, target, "*");
        var factory = new LogFactory { Configuration = config };
        return (factory.GetLogger("test"), target);
    }
}
