using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Power.Tests.Fakes;

/// <summary>
/// Minimal in-memory IRegistryService for Power module tests (DWORD-focused;
/// per-project fake convention). Stores values keyed by "keyPath\valueName".
/// </summary>
public sealed class FakeRegistryService : IRegistryService
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public void SetDWord(string keyPath, string valueName, int value)
        => _values[Key(keyPath, valueName)] = value;

    public int? GetDWord(string keyPath, string valueName)
        => _values.TryGetValue(Key(keyPath, valueName), out var v) && v is int i ? i : null;

    private static string Key(string keyPath, string valueName) => $"{keyPath}\\{valueName}";

    public OperationResult<int> ReadDWord(string keyPath, string valueName)
        => _values.TryGetValue(Key(keyPath, valueName), out var v) && v is int i
            ? OperationResult<int>.Success(i)
            : OperationResult<int>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value)
    {
        Calls.Add($"WriteDWord:{Key(keyPath, valueName)}={value}");
        _values[Key(keyPath, valueName)] = value;
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> DeleteValue(string keyPath, string valueName)
    {
        Calls.Add($"DeleteValue:{Key(keyPath, valueName)}");
        _values.Remove(Key(keyPath, valueName));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<string> ReadString(string keyPath, string valueName)
        => _values.TryGetValue(Key(keyPath, valueName), out var v) && v is string text
            ? OperationResult<string>.Success(text)
            : OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
    public OperationResult<string> ReadExpandString(string keyPath, string valueName)
        => OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName)
        => OperationResult<string[]>.Failure("Not found", ErrorCategory.NotFound);
    public OperationResult<byte[]> ReadBinary(string keyPath, string valueName)
        => OperationResult<byte[]>.Failure("Not found", ErrorCategory.NotFound);
    public OperationResult<bool> WriteString(string keyPath, string valueName, string value)
    {
        Calls.Add($"WriteString:{Key(keyPath, valueName)}={value}");
        _values[Key(keyPath, valueName)] = value;
        return OperationResult<bool>.Success(true);
    }
    public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value)
        => OperationResult<bool>.Success(true);
    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values)
        => OperationResult<bool>.Success(true);
    public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value)
        => OperationResult<bool>.Success(true);
    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
        => OperationResult<bool>.Success(true);
    public OperationResult<bool> KeyExists(string keyPath)
        => OperationResult<bool>.Success(false);
    public OperationResult<bool> ValueExists(string keyPath, string valueName)
        => OperationResult<bool>.Success(_values.ContainsKey(Key(keyPath, valueName)));
    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath)
        => OperationResult<IReadOnlyList<string>>.Success([]);
    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath)
        => OperationResult<IReadOnlyList<string>>.Success([]);
    public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName)
        => OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
}
