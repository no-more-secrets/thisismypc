using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

/// <summary>
/// In-memory IRegistryService whose writes persist and read back — for end-to-end tests
/// that need staged changes to actually land somewhere. (The plain FakeRegistryService
/// is a no-op used where registry state is irrelevant.)
/// </summary>
internal sealed class StoringFakeRegistryService : IRegistryService
{
    private readonly Dictionary<(string Key, string Value), object> _values =
        new(EqualityComparer<(string, string)>.Default);
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public OperationResult<int> ReadDWord(string keyPath, string valueName) =>
        _values.TryGetValue((Norm(keyPath), valueName), out var v) && v is int i
            ? OperationResult<int>.Success(i)
            : OperationResult<int>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string> ReadString(string keyPath, string valueName) =>
        _values.TryGetValue((Norm(keyPath), valueName), out var v) && v is string s
            ? OperationResult<string>.Success(s)
            : OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string> ReadExpandString(string keyPath, string valueName) =>
        ReadString(keyPath, valueName);

    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) =>
        OperationResult<string[]>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) =>
        _values.TryGetValue((Norm(keyPath), valueName), out var v) && v is byte[] b
            ? OperationResult<byte[]>.Success(b)
            : OperationResult<byte[]>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value)
    {
        _values[(Norm(keyPath), valueName)] = value;
        _keys.Add(Norm(keyPath));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value)
    {
        _values[(Norm(keyPath), valueName)] = value;
        _keys.Add(Norm(keyPath));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteString(string keyPath, string valueName, string value)
    {
        _values[(Norm(keyPath), valueName)] = value;
        _keys.Add(Norm(keyPath));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) =>
        WriteString(keyPath, valueName, value);

    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> DeleteValue(string keyPath, string valueName)
    {
        _values.Remove((Norm(keyPath), valueName));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
    {
        _keys.Remove(Norm(keyPath));
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> KeyExists(string keyPath) =>
        OperationResult<bool>.Success(_keys.Contains(Norm(keyPath)));

    public OperationResult<bool> ValueExists(string keyPath, string valueName) =>
        OperationResult<bool>.Success(_values.ContainsKey((Norm(keyPath), valueName)));

    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) =>
        OperationResult<IReadOnlyList<string>>.Success(Array.Empty<string>());

    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) =>
        OperationResult<IReadOnlyList<string>>.Success(Array.Empty<string>());

    public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) =>
        ReadString(keyPath, valueName);

    private static string Norm(string keyPath) => keyPath.ToUpperInvariant();
}
