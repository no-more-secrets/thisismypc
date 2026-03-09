namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record EnvironmentScanData(
    IReadOnlyList<EnvironmentVariable> UserVariables,
    IReadOnlyList<EnvironmentVariable> SystemVariables,
    string? UserScanError = null,
    string? SystemScanError = null);
