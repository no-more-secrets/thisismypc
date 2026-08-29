using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Service;

/// <summary>28-1 placeholder — the real post-reboot watchdog arrives with 28-3.</summary>
public sealed class EmptyDriftReportSource : IDriftReportSource
{
    public bool BaselinePresent => false;
    public DateTimeOffset? LastScanUtc => null;
    public DriftReportResponse GetReport() => new() { BaselinePresent = false };
}
