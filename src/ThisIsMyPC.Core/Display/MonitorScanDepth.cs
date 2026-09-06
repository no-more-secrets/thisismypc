namespace ThisIsMyPC.Core.Display;

/// <summary>
/// How much of a monitor's DDC state a scan reads. Quick is the three live
/// values (brightness, contrast, input) plus whatever the service already
/// knows about the monitor's features; it is what a page open can afford.
/// Full also requests the capabilities string and probes every vendor code,
/// which takes seconds per monitor and belongs in the background.
/// </summary>
public enum MonitorScanDepth
{
    Quick,
    Full,
}
