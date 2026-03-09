using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests.Services;

public sealed class EnvironmentVariableReaderTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly EnvironmentVariableReader _sut;

    public EnvironmentVariableReaderTests()
    {
        _sut = new EnvironmentVariableReader(_registry);
    }

    [Fact]
    public void ReadUserVariables_returns_all_user_vars()
    {
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "PATH", @"C:\bin");
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "HOME", @"C:\Users\test");
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "TEMP", @"C:\Temp");

        var result = _sut.ReadUserVariables();

        Assert.Equal(3, result.Count);
        Assert.All(result, v => Assert.Equal(EnvironmentVariableScope.User, v.Scope));
        Assert.Contains(result, v => v.Name == "PATH" && v.Value == @"C:\bin");
        Assert.Contains(result, v => v.Name == "HOME" && v.Value == @"C:\Users\test");
        Assert.Contains(result, v => v.Name == "TEMP" && v.Value == @"C:\Temp");
    }

    [Fact]
    public void ReadUserVariables_returns_empty_when_key_missing()
    {
        // No setup — key doesn't exist
        var result = _sut.ReadUserVariables();

        Assert.Empty(result);
    }

    [Fact]
    public void ReadSystemVariables_returns_all_system_vars()
    {
        _registry.SetString(EnvironmentVariableReader.SystemEnvKeyPath, "ComSpec", @"C:\Windows\system32\cmd.exe");
        _registry.SetString(EnvironmentVariableReader.SystemEnvKeyPath, "OS", "Windows_NT");

        var result = _sut.ReadSystemVariables();

        Assert.Equal(2, result.Count);
        Assert.All(result, v => Assert.Equal(EnvironmentVariableScope.System, v.Scope));
        Assert.Contains(result, v => v.Name == "ComSpec");
        Assert.Contains(result, v => v.Name == "OS");
    }

    [Fact]
    public void ReadVariables_handles_empty_value()
    {
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "EMPTY_VAR", "");

        var result = _sut.ReadUserVariables();

        Assert.Single(result);
        Assert.Equal("", result[0].Value);
    }

    [Fact]
    public void ReadVariables_preserves_expand_string_references()
    {
        // FakeRegistryService's ReadExpandString delegates to ReadString (no expansion)
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "MY_PATH", @"%SystemRoot%\system32");

        var result = _sut.ReadUserVariables();

        Assert.Single(result);
        Assert.Equal(@"%SystemRoot%\system32", result[0].Value);
    }
}
