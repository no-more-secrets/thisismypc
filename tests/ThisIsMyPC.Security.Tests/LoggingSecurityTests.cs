using System.Text.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace ThisIsMyPC.Security.Tests;

[Trait("Category", "Security")]
public class LoggingSecurityTests
{
    [Fact]
    public void ClefFormatter_ProducesValidJsonWithEnvelope()
    {
        var (logger, writer) = CreateClefLogger();
        using (logger)
            logger.Information("Test {Value}", "hello");

        var output = writer.ToString().Trim();
        Assert.False(string.IsNullOrWhiteSpace(output));

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("@t", out _), "CLEF envelope missing @t (timestamp)");
        Assert.True(root.TryGetProperty("@mt", out _), "CLEF envelope missing @mt (message template)");
        Assert.Equal("hello", root.GetProperty("Value").GetString());
    }

    [Fact]
    public void ClefFormatter_CrlfInPropertyValue_ProducesSingleLine_CWE117()
    {
        var (logger, writer) = CreateClefLogger();
        using (logger)
            logger.Information("User action: {Input}", "legit\r\n[WARN] Fake security event");

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        var input = doc.RootElement.GetProperty("Input").GetString();
        Assert.Contains("\r\n", input, StringComparison.Ordinal);
        Assert.Contains("[WARN] Fake security event", input, StringComparison.Ordinal);
    }

    [Fact]
    public void ClefFormatter_SpecialCharactersInValues_JsonEscaped()
    {
        var (logger, writer) = CreateClefLogger();
        using (logger)
            logger.Information("Path: {FilePath}", "C:\\Users\\test\\file \"quoted\".txt");

        var output = writer.ToString().Trim();
        using var doc = JsonDocument.Parse(output);
        var path = doc.RootElement.GetProperty("FilePath").GetString();
        Assert.Equal("C:\\Users\\test\\file \"quoted\".txt", path);
    }

    [Fact]
    public void ClefFormatter_OutputIsJsonObject_NotPlainText()
    {
        var (logger, writer) = CreateClefLogger();
        using (logger)
            logger.Information("Startup check {Status}", "OK");

        var output = writer.ToString().Trim();

        Assert.StartsWith("{", output, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(output);
        Assert.Equal("OK", doc.RootElement.GetProperty("Status").GetString());
    }

    private static (Logger logger, StringWriter writer) CreateClefLogger()
    {
        var writer = new StringWriter();
        var logger = new LoggerConfiguration()
            .WriteTo.Sink(new FormatterSink(writer, new CompactJsonFormatter()))
            .CreateLogger();
        return (logger, writer);
    }

    private sealed class FormatterSink(StringWriter writer, ITextFormatter formatter) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => formatter.Format(logEvent, writer);
    }
}
