using ThisIsMyPC.Ipc.Contracts;

namespace ThisIsMyPC.Service;

/// <summary>Provides the current drift report to the pipe server (filled in by 28-3).</summary>
public interface IDriftReportSource
{
    bool BaselinePresent { get; }
    DateTimeOffset? LastScanUtc { get; }
    DriftReportResponse GetReport();
}
