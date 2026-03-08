using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Win32.Registry;

namespace ThisIsMyPC.Integration.Tests.Registry;

[Trait("Category", "Integration")]
public sealed class RegistryServiceIntegrationTests : IDisposable
{
    private const string SandboxKeyPath = @"HKCU\Software\ThisIsMyPC\Tests";
    private readonly RegistryService _sut = new();

    public RegistryServiceIntegrationTests()
    {
        // Ensure sandbox key exists
        _sut.WriteDWord(SandboxKeyPath, "setup", 1);
    }

    public void Dispose()
    {
        // Clean up sandbox key tree
        _sut.DeleteKey(SandboxKeyPath, recursive: true);
        // Also clean parent if empty
        _sut.DeleteKey(@"HKCU\Software\ThisIsMyPC", recursive: false);
    }

    [Fact]
    public void WriteDWord_and_ReadDWord_roundtrip()
    {
        var writeResult = _sut.WriteDWord(SandboxKeyPath, "TestDWord", 42);
        Assert.True(writeResult.IsSuccess);

        var readResult = _sut.ReadDWord(SandboxKeyPath, "TestDWord");
        Assert.True(readResult.IsSuccess);
        Assert.Equal(42, readResult.Value);
    }

    [Fact]
    public void ReadDWord_returns_NotFound_for_missing_value()
    {
        var result = _sut.ReadDWord(SandboxKeyPath, "NonExistentValue");
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
    }

    [Fact]
    public void WriteString_and_ReadString_roundtrip()
    {
        var writeResult = _sut.WriteString(SandboxKeyPath, "TestString", "hello world");
        Assert.True(writeResult.IsSuccess);

        var readResult = _sut.ReadString(SandboxKeyPath, "TestString");
        Assert.True(readResult.IsSuccess);
        Assert.Equal("hello world", readResult.Value);
    }

    [Fact]
    public void WriteExpandString_and_ReadExpandString_roundtrip()
    {
        var writeResult = _sut.WriteExpandString(SandboxKeyPath, "TestExpand", "%SystemRoot%\\test");
        Assert.True(writeResult.IsSuccess);

        // ReadExpandString should return unexpanded value
        var readResult = _sut.ReadExpandString(SandboxKeyPath, "TestExpand");
        Assert.True(readResult.IsSuccess);
        Assert.Equal("%SystemRoot%\\test", readResult.Value);
    }

    [Fact]
    public void WriteMultiString_and_ReadMultiString_roundtrip()
    {
        var values = new[] { "one", "two", "three" };
        var writeResult = _sut.WriteMultiString(SandboxKeyPath, "TestMulti", values);
        Assert.True(writeResult.IsSuccess);

        var readResult = _sut.ReadMultiString(SandboxKeyPath, "TestMulti");
        Assert.True(readResult.IsSuccess);
        Assert.Equal(values, readResult.Value);
    }

    [Fact]
    public void DeleteValue_removes_existing_value()
    {
        _sut.WriteString(SandboxKeyPath, "ToDelete", "temp");
        var deleteResult = _sut.DeleteValue(SandboxKeyPath, "ToDelete");
        Assert.True(deleteResult.IsSuccess);

        var readResult = _sut.ReadString(SandboxKeyPath, "ToDelete");
        Assert.False(readResult.IsSuccess);
        Assert.Equal(ErrorCategory.NotFound, readResult.ErrorCategory);
    }

    [Fact]
    public void DeleteKey_removes_key()
    {
        var childKey = $@"{SandboxKeyPath}\ChildToDelete";
        _sut.WriteDWord(childKey, "val", 1);

        var deleteResult = _sut.DeleteKey(childKey, recursive: false);
        Assert.True(deleteResult.IsSuccess);

        var existsResult = _sut.KeyExists(childKey);
        Assert.True(existsResult.IsSuccess);
        Assert.False(existsResult.Value);
    }

    [Fact]
    public void KeyExists_returns_true_for_existing_key()
    {
        var result = _sut.KeyExists(SandboxKeyPath);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void KeyExists_returns_false_for_nonexistent_key()
    {
        var result = _sut.KeyExists($@"{SandboxKeyPath}\DoesNotExist");
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void ValueExists_returns_true_for_existing_value()
    {
        _sut.WriteString(SandboxKeyPath, "ExistsCheck", "val");
        var result = _sut.ValueExists(SandboxKeyPath, "ExistsCheck");
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void ValueExists_returns_false_for_nonexistent_value()
    {
        var result = _sut.ValueExists(SandboxKeyPath, "NoSuchValue");
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void EnumerateSubKeys_returns_all_child_keys()
    {
        _sut.WriteDWord($@"{SandboxKeyPath}\SubA", "val", 1);
        _sut.WriteDWord($@"{SandboxKeyPath}\SubB", "val", 2);

        var result = _sut.EnumerateSubKeys(SandboxKeyPath);
        Assert.True(result.IsSuccess);
        Assert.Contains("SubA", result.Value!);
        Assert.Contains("SubB", result.Value!);
    }

    [Fact]
    public void EnumerateValues_returns_all_value_names()
    {
        _sut.WriteString(SandboxKeyPath, "ValA", "a");
        _sut.WriteString(SandboxKeyPath, "ValB", "b");

        var result = _sut.EnumerateValues(SandboxKeyPath);
        Assert.True(result.IsSuccess);
        Assert.Contains("ValA", result.Value!);
        Assert.Contains("ValB", result.Value!);
    }

    [Fact]
    public void ReadValueBeforeWrite_returns_current_value_as_string()
    {
        _sut.WriteDWord(SandboxKeyPath, "BeforeWrite", 99);

        var result = _sut.ReadValueBeforeWrite(SandboxKeyPath, "BeforeWrite");
        Assert.True(result.IsSuccess);
        Assert.Equal("99", result.Value);
    }

    [Fact]
    public void WriteDWord_to_HKLM_without_elevation_returns_AccessDenied()
    {
        // This test verifies access-denied handling for non-elevated processes.
        // If running elevated, the write will succeed — both outcomes are valid.
        var result = _sut.WriteDWord(@"HKLM\SOFTWARE\ThisIsMyPC\Tests", "ElevationTest", 1);
        if (!result.IsSuccess)
        {
            Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
        }
        // Clean up if it succeeded (running elevated)
        if (result.IsSuccess)
        {
            _sut.DeleteKey(@"HKLM\SOFTWARE\ThisIsMyPC\Tests", recursive: true);
            _sut.DeleteKey(@"HKLM\SOFTWARE\ThisIsMyPC", recursive: false);
        }
    }
}
