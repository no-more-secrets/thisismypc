using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>
/// In-memory registry for view tests, so a page can be rendered without
/// touching the live machine. Unset values read as not found, which is what
/// a fresh machine looks like.
/// </summary>
public sealed class UiFakeRegistryService : IRegistryService
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(string keyPath, string valueName) => $@"{keyPath}\{valueName}";

    public void SetDWord(string keyPath, string valueName, int value)
    {
        _values[Key(keyPath, valueName)] = value;
        _keys.Add(keyPath);
    }

    public void AddKey(string keyPath) => _keys.Add(keyPath);

    public OperationResult<int> ReadDWord(string keyPath, string valueName) =>
        _values.TryGetValue(Key(keyPath, valueName), out var value) && value is int number
            ? OperationResult<int>.Success(number)
            : OperationResult<int>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string> ReadString(string keyPath, string valueName) =>
        _values.TryGetValue(Key(keyPath, valueName), out var value) && value is string text
            ? OperationResult<string>.Success(text)
            : OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string> ReadExpandString(string keyPath, string valueName) =>
        OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) =>
        OperationResult<string[]>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) =>
        OperationResult<byte[]>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value)
    {
        SetDWord(keyPath, valueName, value);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteString(string keyPath, string valueName, string value)
    {
        _values[Key(keyPath, valueName)] = value;
        _keys.Add(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> DeleteValue(string keyPath, string valueName)
    {
        _values.Remove(Key(keyPath, valueName));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
    {
        _keys.Remove(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> KeyExists(string keyPath) =>
        OperationResult<bool>.Success(_keys.Contains(keyPath));

    public OperationResult<bool> ValueExists(string keyPath, string valueName) =>
        OperationResult<bool>.Success(_values.ContainsKey(Key(keyPath, valueName)));

    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) =>
        OperationResult<IReadOnlyList<string>>.Success([]);

    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) =>
        OperationResult<IReadOnlyList<string>>.Success([]);

    public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) =>
        OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
}
