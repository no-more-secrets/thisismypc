using ThisIsMyPC.Interop.Win32.Registry;

namespace ThisIsMyPC.Integration.Tests.Registry;

[Trait("Category", "Integration")]
public sealed class EnvironmentVariableIntegrationTests : IDisposable
{
    private const string SandboxKeyPath = @"HKCU\Software\ThisIsMyPC\TestsEnvVars\Environment";
    private readonly RegistryService _sut = new();

    public EnvironmentVariableIntegrationTests()
    {
        // Ensure sandbox key exists
        _sut.WriteDWord(SandboxKeyPath, "setup", 1);
    }

    public void Dispose()
    {
        _sut.DeleteKey(@"HKCU\Software\ThisIsMyPC\TestsEnvVars", recursive: true);
    }

    [Fact]
    public void WriteExpandString_ReadExpandString_roundtrip()
    {
        var writeResult = _sut.WriteExpandString(SandboxKeyPath, "TESTVAR", "hello-world");
        Assert.True(writeResult.IsSuccess);

        var readResult = _sut.ReadExpandString(SandboxKeyPath, "TESTVAR");
        Assert.True(readResult.IsSuccess);
        Assert.Equal("hello-world", readResult.Value);
    }

    [Fact]
    public void DeleteValue_removes_variable()
    {
        _sut.WriteExpandString(SandboxKeyPath, "TO_DELETE", "temp-value");

        var deleteResult = _sut.DeleteValue(SandboxKeyPath, "TO_DELETE");
        Assert.True(deleteResult.IsSuccess);

        var readResult = _sut.ReadExpandString(SandboxKeyPath, "TO_DELETE");
        Assert.False(readResult.IsSuccess);
    }

    [Fact]
    public void EnumerateValues_lists_all_variables()
    {
        _sut.WriteExpandString(SandboxKeyPath, "VAR_A", "a");
        _sut.WriteExpandString(SandboxKeyPath, "VAR_B", "b");
        _sut.WriteExpandString(SandboxKeyPath, "VAR_C", "c");

        var result = _sut.EnumerateValues(SandboxKeyPath);
        Assert.True(result.IsSuccess);
        Assert.Contains("VAR_A", result.Value!);
        Assert.Contains("VAR_B", result.Value!);
        Assert.Contains("VAR_C", result.Value!);
    }

    [Fact]
    public void WriteExpandString_preserves_percent_references()
    {
        _sut.WriteExpandString(SandboxKeyPath, "EXPAND_TEST", @"%SystemRoot%\system32");

        var result = _sut.ReadExpandString(SandboxKeyPath, "EXPAND_TEST");
        Assert.True(result.IsSuccess);
        Assert.Equal(@"%SystemRoot%\system32", result.Value);
    }
}
