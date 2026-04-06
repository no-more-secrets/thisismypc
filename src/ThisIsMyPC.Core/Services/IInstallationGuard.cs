namespace ThisIsMyPC.Core.Services;

public interface IInstallationGuard
{
    bool IsProtectedLocation { get; }
    string? WarningMessage { get; }
}
