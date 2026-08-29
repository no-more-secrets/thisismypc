using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

/// <summary>
/// Minimal fake for tests that require IRegistryService as a constructor dependency
/// but don't exercise registry operations. For full-featured fake, see
/// ThisIsMyPC.Modules.Shell.Tests.Fakes.FakeRegistryService.
/// </summary>
internal sealed class FakeRegistryService : IRegistryService
{
    /// <summary>Keys that KeyExists reports as present (case-insensitive).</summary>
    public HashSet<string> ExistingKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every DeleteKey call, in order.</summary>
    public List<(string KeyPath, bool Recursive)> DeletedKeys { get; } = [];

    /// <summary>When set, DeleteKey on this path fails with AccessDenied.</summary>
    public string? FailDeleteForPath { get; set; }

    public OperationResult<int> ReadDWord(string keyPath, string valueName) =>
        OperationResult<int>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string> ReadString(string keyPath, string valueName) =>
        OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string> ReadExpandString(string keyPath, string valueName) =>
        OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) =>
        OperationResult<string[]>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) =>
        OperationResult<byte[]>.Failure("Not found", ErrorCategory.NotFound);

    public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> WriteString(string keyPath, string valueName, string value) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> DeleteValue(string keyPath, string valueName) =>
        OperationResult<bool>.Success(true);

    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
    {
        if (string.Equals(FailDeleteForPath, keyPath, StringComparison.OrdinalIgnoreCase))
            return OperationResult<bool>.Failure("Access denied", ErrorCategory.AccessDenied);

        DeletedKeys.Add((keyPath, recursive));
        ExistingKeys.Remove(keyPath);
        return OperationResult<bool>.Success(true);
    }

    public OperationResult<bool> KeyExists(string keyPath) =>
        OperationResult<bool>.Success(ExistingKeys.Contains(keyPath));

    public OperationResult<bool> ValueExists(string keyPath, string valueName) =>
        OperationResult<bool>.Success(false);

    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) =>
        OperationResult<IReadOnlyList<string>>.Success(Array.Empty<string>());

    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) =>
        OperationResult<IReadOnlyList<string>>.Success(Array.Empty<string>());

    public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) =>
        OperationResult<string>.Failure("Not found", ErrorCategory.NotFound);
}
