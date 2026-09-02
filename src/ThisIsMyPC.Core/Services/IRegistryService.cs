using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

public interface IRegistryService
{
    OperationResult<int> ReadDWord(string keyPath, string valueName);
    OperationResult<string> ReadString(string keyPath, string valueName);
    OperationResult<string> ReadExpandString(string keyPath, string valueName);
    OperationResult<string[]> ReadMultiString(string keyPath, string valueName);
    OperationResult<byte[]> ReadBinary(string keyPath, string valueName);
    OperationResult<bool> WriteDWord(string keyPath, string valueName, int value);
    OperationResult<bool> WriteString(string keyPath, string valueName, string value);
    OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value);
    OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values);
    OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value);
    OperationResult<bool> DeleteValue(string keyPath, string valueName);
    OperationResult<bool> DeleteKey(string keyPath, bool recursive = false);
    OperationResult<bool> KeyExists(string keyPath);
    OperationResult<bool> ValueExists(string keyPath, string valueName);
    OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath);
    OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath);
    OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName);

    /// <summary>
    /// Reads a value of any type with its type, for moving it elsewhere intact.
    /// The default probes the typed readers in turn (test fakes store typed
    /// objects, so that is exact for them); the Win32 service overrides it
    /// with one typed read.
    /// </summary>
    OperationResult<RegistryValueData> ReadValue(string keyPath, string valueName)
    {
        if (ReadBinary(keyPath, valueName) is { IsSuccess: true, Value: { } bytes })
            return OperationResult<RegistryValueData>.Success(RegistryValueData.FromBinary(bytes));
        if (ReadDWord(keyPath, valueName) is { IsSuccess: true, Value: var number })
            return OperationResult<RegistryValueData>.Success(RegistryValueData.FromDWord(number));
        if (ReadMultiString(keyPath, valueName) is { IsSuccess: true, Value: { } lines })
            return OperationResult<RegistryValueData>.Success(RegistryValueData.FromMultiString(lines));
        if (ReadString(keyPath, valueName) is { IsSuccess: true, Value: { } text })
            return OperationResult<RegistryValueData>.Success(RegistryValueData.FromString(text));
        return OperationResult<RegistryValueData>.Failure($"Value not found: {keyPath}\\{valueName}", ErrorCategory.NotFound);
    }

    /// <summary>Writes a value with the type it was read with.</summary>
    OperationResult<bool> WriteValue(string keyPath, string valueName, RegistryValueData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Kind switch
        {
            RegistryValueDataKind.Binary => WriteBinary(keyPath, valueName, value.AsBinary()),
            RegistryValueDataKind.DWord => WriteDWord(keyPath, valueName, value.AsDWord()),
            RegistryValueDataKind.QWord => WriteBinary(keyPath, valueName, BitConverter.GetBytes(value.AsQWord())),
            RegistryValueDataKind.MultiString => WriteMultiString(keyPath, valueName, value.AsMultiString()),
            RegistryValueDataKind.ExpandString => WriteExpandString(keyPath, valueName, value.Data),
            _ => WriteString(keyPath, valueName, value.Data),
        };
    }

    /// <summary>When the key was last written (local time); null when unknown or unsupported (the default).</summary>
    DateTime? GetLastWriteTime(string keyPath) => null;

    /// <summary>Creates an empty key (no-op when present). The default writes and removes a marker value.</summary>
    OperationResult<bool> CreateKey(string keyPath)
    {
        if (KeyExists(keyPath) is { IsSuccess: true, Value: true })
            return OperationResult<bool>.Success(true);
        const string marker = "__thisismypc_create";
        var write = WriteString(keyPath, marker, string.Empty);
        if (!write.IsSuccess)
            return write;
        return DeleteValue(keyPath, marker);
    }
}
