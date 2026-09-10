namespace ThisIsMyPC.Core.Services;

/// <summary>Outcome of trying to activate a companion without launching another process.</summary>
public enum CompanionWindowOutcome
{
    NotRunning,
    Activated,
    RunningWithoutAccessibleWindow,
}

/// <summary>Activates a matching companion in the current desktop session. Never starts or elevates a process.</summary>
public interface ICompanionWindowService
{
    CompanionWindowOutcome TryActivate(string executablePath);
}
