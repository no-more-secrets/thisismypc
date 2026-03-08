namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record EnvironmentVariable(
    string Name,
    string Value,
    EnvironmentVariableScope Scope);

public enum EnvironmentVariableScope { User, System }
