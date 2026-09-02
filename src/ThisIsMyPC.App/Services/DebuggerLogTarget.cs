using System.Diagnostics;
using NLog;
using NLog.Targets;

namespace ThisIsMyPC.App.Services;

/// <summary>
/// Sends every event to the attached debugger, so the Visual Studio Output
/// window shows the same lines as the Debug log console. Without a debugger
/// the target does nothing. Registered by instance, never by name, so
/// NativeAOT has nothing to reflect over.
/// </summary>
public sealed class DebuggerLogTarget : TargetWithLayout
{
    protected override void Write(LogEventInfo logEvent)
    {
        if (!Debugger.IsAttached)
            return;
        Debugger.Log(logEvent.Level.Ordinal, logEvent.LoggerName, RenderLogEvent(Layout, logEvent) + Environment.NewLine);
    }
}
