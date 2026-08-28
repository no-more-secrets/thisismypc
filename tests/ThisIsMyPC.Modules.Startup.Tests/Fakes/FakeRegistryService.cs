using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Startup.Tests.Fakes;

/// <summary>
/// Manual fake for IRegistryService. NativeAOT-safe, no mocking framework needed.
/// Stores values in a dictionary keyed by "keyPath\valueName".
/// </summary>
public sealed class FakeRegistryService : IRegistryService
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ErrorCategory> _writeFailures = new(StringComparer.OrdinalIgnoreCase);

    public void SetWriteFailure(string keyPath, ErrorCategory error)
    {
        _writeFailures[keyPath] = error;
    }

    public void SetDWord(string keyPath, string valueName, int value)
    {
        _values[MakeKey(keyPath, valueName)] = value;
        _keys.Add(keyPath);
    }

    public void SetString(string keyPath, string valueName, string value)
    {
        _values[MakeKey(keyPath, valueName)] = value;
        _keys.Add(keyPath);
    }

    public void SetBinary(string keyPath, string valueName, byte[] value)
    {
        _values[MakeKey(keyPath, valueName)] = value;
        _keys.Add(keyPath);
    }

    public void AddKey(string keyPath)
    {
        _keys.Add(keyPath);
    }

    public OperationResult<int> ReadDWord(string keyPath, string valueName)
    {
        if (_values.TryGetValue(MakeKey(keyPath, valueName), out var val) && val is int intVal)
            return OperationResult<int>.Success(intVal);
        return OperationResult<int>.Failure("Not found", ErrorCategory.NotFound);
    }

    public OperationResult<string> ReadString(string keyPath, string valueName)
    {
        if (_values.TryGetValue(MakeKey(keyPath, valueName), out var val) && val is string strVal)
            return OperationResult<string>.Success(strVal);
        return OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
    }

    public OperationResult<string> ReadExpandString(string keyPath, string valueName)
        => ReadString(keyPath, valueName);

    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName)
    {
        if (_values.TryGetValue(MakeKey(keyPath, valueName), out var val) && val is string[] arr)
            return OperationResult<string[]>.Success(arr);
        return OperationResult<string[]>.Failure("Not found", ErrorCategory.NotFound);
    }

    public OperationResult<byte[]> ReadBinary(string keyPath, string valueName)
    {
        if (_values.TryGetValue(MakeKey(keyPath, valueName), out var val) && val is byte[] bytes)
            return OperationResult<byte[]>.Success(bytes);
        return OperationResult<byte[]>.Failure("Not found", ErrorCategory.NotFound);
    }

    public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value)
    {
        if (_writeFailures.TryGetValue(keyPath, out var error))
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", error);
        _values[MakeKey(keyPath, valueName)] = value;
        _keys.Add(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value)
    {
        if (_writeFailures.TryGetValue(keyPath, out var error))
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", error);
        _values[MakeKey(keyPath, valueName)] = value;
        _keys.Add(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteString(string keyPath, string valueName, string value)
    {
        if (_writeFailures.TryGetValue(keyPath, out var error))
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", error);
        _values[MakeKey(keyPath, valueName)] = value;
        _keys.Add(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value)
        => WriteString(keyPath, valueName, value);

    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values)
    {
        _values[MakeKey(keyPath, valueName)] = values;
        _keys.Add(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> DeleteValue(string keyPath, string valueName)
    {
        if (!_keys.Contains(keyPath))
            return OperationResult<bool>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);
        _values.Remove(MakeKey(keyPath, valueName));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
    {
        _keys.Remove(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> KeyExists(string keyPath)
        => OperationResult<bool>.Success(_keys.Contains(keyPath));

    public OperationResult<bool> ValueExists(string keyPath, string valueName)
        => OperationResult<bool>.Success(_values.ContainsKey(MakeKey(keyPath, valueName)));

    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath)
    {
        var prefix = keyPath + "\\";
        var children = _keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .Where(k => !k.Contains('\\'))
            .ToArray();

        if (children.Length == 0 && !_keys.Contains(keyPath))
            return OperationResult<IReadOnlyList<string>>.Failure("Not found", ErrorCategory.NotFound);

        return OperationResult<IReadOnlyList<string>>.Success(children);
    }

    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath)
    {
        var prefix = keyPath + "\\";
        var names = _values.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .Where(k => !k.Contains('\\'))
            .ToArray();

        if (names.Length == 0 && !_keys.Contains(keyPath))
            return OperationResult<IReadOnlyList<string>>.Failure("Not found", ErrorCategory.NotFound);

        return OperationResult<IReadOnlyList<string>>.Success(names);
    }

    public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName)
    {
        if (_values.TryGetValue(MakeKey(keyPath, valueName), out var val))
        {
            return val switch
            {
                string[] arr => OperationResult<string>.Success(string.Join('\0', arr)),
                _ => OperationResult<string>.Success(val.ToString()!)
            };
        }
        return OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
    }

    private static string MakeKey(string keyPath, string valueName) => $"{keyPath}\\{valueName}";
}
