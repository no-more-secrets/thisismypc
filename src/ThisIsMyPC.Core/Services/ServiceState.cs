namespace ThisIsMyPC.Core.Services;

/// <summary>Mirrors the SCM SERVICE_* current-state values.</summary>
public enum ServiceState
{
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused
}
