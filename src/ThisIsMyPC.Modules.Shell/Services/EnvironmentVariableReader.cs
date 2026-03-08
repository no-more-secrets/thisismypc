using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

public sealed class EnvironmentVariableReader
{
    private const string UserEnvKeyPath = @"HKCU\Environment";
    private const string SystemEnvKeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    private readonly IRegistryService _registryService;

    public EnvironmentVariableReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    public IReadOnlyList<EnvironmentVariable> ReadUserVariables()
    {
        return ReadVariables(UserEnvKeyPath, EnvironmentVariableScope.User);
    }

    public IReadOnlyList<EnvironmentVariable> ReadSystemVariables()
    {
        return ReadVariables(SystemEnvKeyPath, EnvironmentVariableScope.System);
    }

    private IReadOnlyList<EnvironmentVariable> ReadVariables(string keyPath, EnvironmentVariableScope scope)
    {
        var namesResult = _registryService.EnumerateValues(keyPath);
        if (!namesResult.IsSuccess)
            return [];

        var variables = new List<EnvironmentVariable>();
        foreach (var name in namesResult.Value!)
        {
            var valueResult = _registryService.ReadExpandString(keyPath, name);
            var value = valueResult.IsSuccess ? valueResult.Value! : string.Empty;
            variables.Add(new EnvironmentVariable(name, value, scope));
        }

        return variables;
    }
}
