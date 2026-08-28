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
}
