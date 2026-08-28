using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;
using ThisIsMyPC.Modules.Shell.Services;
using ThisIsMyPC.Modules.Shell.Tests.Fakes;

namespace ThisIsMyPC.Modules.Shell.Tests;

public sealed class EnvironmentModuleTests
{
    private readonly FakeRegistryService _registry = new();
    private readonly FakeEnvironmentBroadcaster _broadcaster = new();
    private readonly EnvironmentModule _module;

    public EnvironmentModuleTests()
    {
        _module = new EnvironmentModule(_registry, _broadcaster);
    }

    [Fact]
    public async Task ScanSystemStateAsync_returns_EnvironmentScanData()
    {
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "TESTVAR", "value1");
        _registry.SetString(EnvironmentVariableReader.SystemEnvKeyPath, "SYS_VAR", "value2");

        var result = await _module.ScanSystemStateAsync();

        Assert.True(result.IsSuccess);
        var scanData = Assert.IsType<EnvironmentScanData>(result.Value);
        Assert.Single(scanData.UserVariables);
        Assert.Equal("TESTVAR", scanData.UserVariables[0].Name);
        Assert.Equal(EnvironmentVariableScope.User, scanData.UserVariables[0].Scope);
        Assert.Single(scanData.SystemVariables);
        Assert.Equal("SYS_VAR", scanData.SystemVariables[0].Name);
        Assert.Equal(EnvironmentVariableScope.System, scanData.SystemVariables[0].Scope);
    }

    [Fact]
    public async Task ApplyChange_Modify_writes_expand_string()
    {
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "MYVAR", "old");

        var change = new ChangeDescriptor
        {
            ModuleId = "Environment",
            SettingId = "env-user-myvar",
            DisplayName = "Environment variable: MYVAR",
            SystemLocation = $@"{EnvironmentVariableReader.UserEnvKeyPath}\MYVAR",
            BeforeValue = "old",
            AfterValue = "new",
            BeforeDisplay = "old",
            AfterDisplay = "new",
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Modify,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readBack = _registry.ReadExpandString(EnvironmentVariableReader.UserEnvKeyPath, "MYVAR");
        Assert.Equal("new", readBack.Value);
        Assert.Equal(1, _broadcaster.CallCount);
    }

    [Fact]
    public async Task ApplyChange_Create_writes_new_value()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Environment",
            SettingId = "env-user-newvar",
            DisplayName = "Environment variable: NEWVAR",
            SystemLocation = $@"{EnvironmentVariableReader.UserEnvKeyPath}\NEWVAR",
            BeforeValue = "",
            AfterValue = "created-value",
            BeforeDisplay = "(new)",
            AfterDisplay = "created-value",
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Create,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readBack = _registry.ReadExpandString(EnvironmentVariableReader.UserEnvKeyPath, "NEWVAR");
        Assert.Equal("created-value", readBack.Value);
        Assert.Equal(1, _broadcaster.CallCount);
    }

    [Fact]
    public async Task ApplyChange_Delete_removes_value()
    {
        _registry.SetString(EnvironmentVariableReader.UserEnvKeyPath, "DELVAR", "to-delete");

        var change = new ChangeDescriptor
        {
            ModuleId = "Environment",
            SettingId = "env-user-delvar",
            DisplayName = "Environment variable: DELVAR",
            SystemLocation = $@"{EnvironmentVariableReader.UserEnvKeyPath}\DELVAR",
            BeforeValue = "to-delete",
            AfterValue = null,
            BeforeDisplay = "to-delete",
            AfterDisplay = "(deleted)",
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Delete,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
        var readBack = _registry.ReadExpandString(EnvironmentVariableReader.UserEnvKeyPath, "DELVAR");
        Assert.False(readBack.IsSuccess);
        Assert.Equal(1, _broadcaster.CallCount);
    }

    [Fact]
    public async Task ApplyChange_Delete_nonexistent_returns_success()
    {
        var change = new ChangeDescriptor
        {
            ModuleId = "Environment",
            SettingId = "env-user-ghost",
            DisplayName = "Environment variable: GHOST",
            SystemLocation = $@"{EnvironmentVariableReader.UserEnvKeyPath}\GHOST",
            BeforeValue = "phantom",
            AfterValue = null,
            BeforeDisplay = "phantom",
            AfterDisplay = "(deleted)",
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Delete,
        };

        var result = await _module.ApplyChangeAsync(change);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ApplyChange_does_not_broadcast_on_failure()
    {
        // Write to a path that will fail (HKLM requires elevation)
        // Simulate by using a registry service that returns failure
        var failingRegistry = new FailOnWriteRegistryService();
        var module = new EnvironmentModule(failingRegistry, _broadcaster);

        var change = new ChangeDescriptor
        {
            ModuleId = "Environment",
            SettingId = "env-system-test",
            DisplayName = "Environment variable: TEST",
            SystemLocation = $@"{EnvironmentVariableReader.SystemEnvKeyPath}\TEST",
            BeforeValue = "old",
            AfterValue = "new",
            BeforeDisplay = "old",
            AfterDisplay = "new",
            ValueType = ChangeValueType.Environment_Variable,
            Category = ChangeCategory.Modify,
        };

        var result = await module.ApplyChangeAsync(change);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, _broadcaster.CallCount);
    }

    private sealed class FakeEnvironmentBroadcaster : IEnvironmentBroadcaster
    {
        public int CallCount { get; private set; }
        public void BroadcastEnvironmentChange() => CallCount++;
    }

    private sealed class FailOnWriteRegistryService : IRegistryService
    {
        public OperationResult<int> ReadDWord(string keyPath, string valueName) => OperationResult<int>.Failure("not impl", ErrorCategory.NotFound);
        public OperationResult<string> ReadString(string keyPath, string valueName) => OperationResult<string>.Failure("not impl", ErrorCategory.NotFound);
        public OperationResult<string> ReadExpandString(string keyPath, string valueName) => OperationResult<string>.Failure("not impl", ErrorCategory.NotFound);
        public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) => OperationResult<string[]>.Failure("not impl", ErrorCategory.NotFound);
        public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) => OperationResult<byte[]>.Failure("not impl", ErrorCategory.NotFound);
        public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) => OperationResult<bool>.Failure("access denied", ErrorCategory.AccessDenied);
        public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value) => OperationResult<bool>.Failure("access denied", ErrorCategory.AccessDenied);
        public OperationResult<bool> WriteString(string keyPath, string valueName, string value) => OperationResult<bool>.Failure("access denied", ErrorCategory.AccessDenied);
        public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) => OperationResult<bool>.Failure("access denied", ErrorCategory.AccessDenied);
        public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) => OperationResult<bool>.Failure("access denied", ErrorCategory.AccessDenied);
        public OperationResult<bool> DeleteValue(string keyPath, string valueName) => OperationResult<bool>.Failure("access denied", ErrorCategory.AccessDenied);
        public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false) => OperationResult<bool>.Failure("access denied", ErrorCategory.AccessDenied);
        public OperationResult<bool> KeyExists(string keyPath) => OperationResult<bool>.Success(false);
        public OperationResult<bool> ValueExists(string keyPath, string valueName) => OperationResult<bool>.Success(false);
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) => OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) => OperationResult<IReadOnlyList<string>>.Success([]);
        public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) => OperationResult<string>.Failure("not impl", ErrorCategory.NotFound);
    }
}
