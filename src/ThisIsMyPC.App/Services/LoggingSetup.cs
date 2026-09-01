using System.IO;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Builds the NLog configuration in code: no XML file, nothing for NativeAOT
/// to reflect over. Output is one JSON object per line in the shape the old
/// Serilog CLEF sink wrote (@t, @mt, @l, @x plus the event properties at the
/// root), so existing log readers keep working.
/// </summary>
public static class LoggingSetup
{
    /// <summary>Root logger name for the app; components use their own type names.</summary>
    public const string AppLoggerName = "ThisIsMyPC.App";

    /// <summary>Interop loggers are noisy at Info; release builds keep Warn and above from them.</summary>
    private const string InteropLoggerPattern = "ThisIsMyPC.Interop*";

    /// <summary>One JSON object per event, properties at the root, no newlines inside a record.</summary>
    public static JsonLayout CreateJsonLayout()
    {
        var layout = new JsonLayout
        {
            IncludeEventProperties = true,
            MaxRecursionLimit = 2,
            SuppressSpaces = true,
            RenderEmptyObject = false,
        };
        layout.Attributes.Add(new JsonAttribute("@t", "${date:universalTime=true:format=yyyy-MM-ddTHH\\:mm\\:ss.fffffffZ}"));
        layout.Attributes.Add(new JsonAttribute("@mt", "${message:raw=true}"));
        layout.Attributes.Add(new JsonAttribute("@l", "${level}"));
        layout.Attributes.Add(new JsonAttribute("@x", "${exception:format=toString}"));
        layout.Attributes.Add(new JsonAttribute("SourceContext", "${logger}"));
        return layout;
    }

    /// <summary>
    /// Installs the configuration: a daily JSON file under
    /// <c>{dataDir}\logs</c> (10 MB size cap, 7 files kept) and, when asked,
    /// a console target for the Debug log window. Returns the app logger.
    /// Production passes no factory and configures the global LogManager;
    /// tests pass their own LogFactory so parallel test classes that log
    /// through the global manager cannot write into a test's file.
    /// </summary>
    public static Logger Configure(string dataDir, bool verbose, bool console, LogFactory? factory = null)
    {
        factory ??= LogManager.LogFactory;
        var file = new FileTarget("file")
        {
            FileName = Path.Combine(dataDir, "logs", "thisismypc-${shortdate}.log"),
            Layout = CreateJsonLayout(),
            ArchiveAboveSize = 10 * 1024 * 1024,
            MaxArchiveFiles = 7,
        };

        var minimum = verbose ? LogLevel.Trace : LogLevel.Info;

        factory.Setup().LoadConfiguration(builder =>
        {
            // Start from an empty configuration: the builder otherwise merges
            // into whatever LogManager already holds, and a second Configure
            // call (tests, or a future reconfigure) would keep the old targets.
            builder.Configuration = new LoggingConfiguration();

            // Rule order matters: the interop rule must come first. Events
            // below Warn from interop loggers stop there; Warn and above fall
            // through to the catch-all rules.
            if (!verbose)
                builder.ForLogger(InteropLoggerPattern).WriteToNil(finalMinLevel: LogLevel.Warn);

            builder.ForLogger().FilterMinLevel(minimum).WriteTo(file);

            if (console)
            {
                var target = new ConsoleTarget("console")
                {
                    Layout = "[${time} ${level:uppercase=true:padding=-5}] ${logger}${newline}    ${message}${newline}${exception:format=toString}",
                };
                builder.ForLogger().FilterMinLevel(minimum).WriteTo(target);
            }
        });

        return factory.GetLogger(AppLoggerName);
    }
}
