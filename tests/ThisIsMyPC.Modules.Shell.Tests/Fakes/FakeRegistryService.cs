using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Shell.Tests.Fakes;

/// <summary>
/// Manual fake for IRegistryService. NativeAOT-safe, no mocking framework needed.
/// Stores values in a dictionary keyed by "keyPath\valueName".
/// </summary>
public sealed class FakeRegistryService : IRegistryService
{
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ErrorCategory> _writeFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ErrorCategory> _deleteFailures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Configures a key path to fail on write operations with the specified error category.
    /// </summary>
    public void SetWriteFailure(string keyPath, ErrorCategory error)
    {
        _writeFailures[keyPath] = error;
    }

    /// <summary>
    /// Configures a key path to fail on delete operations with the specified error category.
    /// </summary>
    public void SetDeleteFailure(string keyPath, ErrorCategory error)
    {
        _deleteFailures[keyPath] = error;
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

    public void SetMultiString(string keyPath, string valueName, string[] values)
    {
        _values[MakeKey(keyPath, valueName)] = values;
        _keys.Add(keyPath);
    }

    public void AddKey(string keyPath)
    {
        _keys.Add(keyPath);
    }

    public void AddSubKeys(string keyPath, params string[] subKeyNames)
    {
        _keys.Add(keyPath);
        foreach (var name in subKeyNames)
            _keys.Add($@"{keyPath}\{name}");

        // Store the subkey names for EnumerateSubKeys
        _values[$"{keyPath}\\__subkeys__"] = subKeyNames;
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
    {
        return ReadString(keyPath, valueName);
    }

    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName)
    {
        if (_values.TryGetValue(MakeKey(keyPath, valueName), out var val) && val is string[] arr)
            return OperationResult<string[]>.Success(arr);
        return OperationResult<string[]>.Failure("Not found", ErrorCategory.NotFound);
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
    {
        return WriteString(keyPath, valueName, value);
    }

    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values)
    {
        if (_writeFailures.TryGetValue(keyPath, out var error))
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", error);
        _values[MakeKey(keyPath, valueName)] = values;
        _keys.Add(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> DeleteValue(string keyPath, string valueName)
    {
        if (_deleteFailures.TryGetValue(keyPath, out var deleteError))
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", deleteError);
        if (!_keys.Contains(keyPath))
            return OperationResult<bool>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

        _values.Remove(MakeKey(keyPath, valueName));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
    {
        if (recursive)
        {
            // Remove all child keys and their values
            var prefix = keyPath + "\\";
            var childKeys = _keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var childKey in childKeys)
            {
                _keys.Remove(childKey);
                // Remove values under the child key
                var childPrefix = childKey + "\\";
                var childValues = _values.Keys.Where(k => k.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var v in childValues)
                    _values.Remove(v);
            }

            // Remove values directly under this key
            var keyValues = _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var v in keyValues)
                _values.Remove(v);
        }

        _keys.Remove(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> KeyExists(string keyPath)
    {
        return OperationResult<bool>.Success(_keys.Contains(keyPath));
    }

    public OperationResult<bool> ValueExists(string keyPath, string valueName)
    {
        return OperationResult<bool>.Success(_values.ContainsKey(MakeKey(keyPath, valueName)));
    }

    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath)
    {
        var subKeysKey = $"{keyPath}\\__subkeys__";
        if (_values.TryGetValue(subKeysKey, out var val) && val is string[] subKeys)
            return OperationResult<IReadOnlyList<string>>.Success(subKeys);

        // Fallback: find keys that are direct children
        var prefix = keyPath + "\\";
        var children = _keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..])
            .Where(k => !k.Contains('\\'))
            .ToArray();

        if (children.Length > 0)
            return OperationResult<IReadOnlyList<string>>.Success(children);

        if (!_keys.Contains(keyPath))
            return OperationResult<IReadOnlyList<string>>.Failure("Not found", ErrorCategory.NotFound);

        return OperationResult<IReadOnlyList<string>>.Success(Array.Empty<string>());
    }

    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath)
    {
        var prefix = keyPath + "\\";
        var names = _values.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !k.EndsWith("__subkeys__"))
            .Select(k => k[prefix.Length..])
            .ToArray();

        if (!_keys.Contains(keyPath) && names.Length == 0)
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
