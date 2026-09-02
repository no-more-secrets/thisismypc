using System.Security;
using Microsoft.Win32;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Registry;

public sealed partial class RegistryService : IRegistryService
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetLogger("ThisIsMyPC.Interop.Win32.Registry.RegistryService");

    /// <summary>Every write and delete leaves a line: what, where, and whether Windows took it.</summary>
    private static OperationResult<bool> Logged(string operation, string keyPath, string? valueName, OperationResult<bool> result)
    {
        var where = valueName is null ? keyPath : keyPath + "\\" + valueName;
        if (result.IsSuccess)
            Log.Debug("{Operation} {Where}: ok", operation, where);
        else
            Log.Warn("{Operation} {Where}: {Error}", operation, where, result.ErrorMessage);
        return result;
    }

    public OperationResult<int> ReadDWord(string keyPath, string valueName) =>
        Execute(() => ReadValueCore(keyPath, valueName, raw =>
        {
            if (raw is int intVal)
                return intVal;
            return Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
        }), keyPath);

    public OperationResult<string> ReadString(string keyPath, string valueName) =>
        Execute(() => ReadValueCore(keyPath, valueName, raw => raw?.ToString() ?? string.Empty), keyPath);

    public OperationResult<string> ReadExpandString(string keyPath, string valueName) =>
        Execute(() => ReadValueCore(keyPath, valueName, raw => raw?.ToString() ?? string.Empty, doNotExpand: true), keyPath);

    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) =>
        Execute(() => ReadValueCore(keyPath, valueName, raw =>
        {
            if (raw is string[] arr)
                return arr;
            return [raw?.ToString() ?? string.Empty];
        }), keyPath);

    public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) =>
        Execute(() => ReadValueCore(keyPath, valueName, raw =>
        {
            if (raw is byte[] bytes)
                return bytes;
            throw new InvalidCastException($"Value is not REG_BINARY: {keyPath}\\{valueName}");
        }), keyPath);

    public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value) => Logged("WriteDWord", keyPath, valueName,
        Execute(() => WriteValueCore(keyPath, valueName, value, RegistryValueKind.DWord), keyPath));

    public OperationResult<bool> WriteString(string keyPath, string valueName, string value) => Logged("WriteString", keyPath, valueName,
        Execute(() => WriteValueCore(keyPath, valueName, value, RegistryValueKind.String), keyPath));

    public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) => Logged("WriteExpandString", keyPath, valueName,
        Execute(() => WriteValueCore(keyPath, valueName, value, RegistryValueKind.ExpandString), keyPath));

    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) => Logged("WriteMultiString", keyPath, valueName,
        Execute(() => WriteValueCore(keyPath, valueName, values, RegistryValueKind.MultiString), keyPath));

    public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) => Logged("WriteBinary", keyPath, valueName,
        Execute(() => WriteValueCore(keyPath, valueName, value, RegistryValueKind.Binary), keyPath));

    public OperationResult<bool> DeleteValue(string keyPath, string valueName) => Logged("DeleteValue", keyPath, valueName,
        Execute<bool>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: true);
            if (key is null)
                return OperationResult<bool>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            key.DeleteValue(valueName, throwOnMissingValue: false);
            return OperationResult<bool>.Success(true);
        }, keyPath));

    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false) => Logged("DeleteKey", keyPath, null,
        Execute<bool>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);

            // Guard: block recursive deletion of top-level keys under HKLM/HKCR (e.g., "SOFTWARE", "SYSTEM")
            if (recursive && !subKeyPath.Contains('\\') &&
                (root == Microsoft.Win32.Registry.LocalMachine || root == Microsoft.Win32.Registry.ClassesRoot))
            {
                return OperationResult<bool>.Failure(
                    $"Refusing to recursively delete top-level key: {keyPath}",
                    ErrorCategory.AccessDenied);
            }

            if (recursive)
                root.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
            else
                root.DeleteSubKey(subKeyPath, throwOnMissingSubKey: false);

            return OperationResult<bool>.Success(true);
        }, keyPath));

    public OperationResult<bool> KeyExists(string keyPath) =>
        Execute<bool>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            return OperationResult<bool>.Success(key is not null);
        }, keyPath);

    public OperationResult<bool> ValueExists(string keyPath, string valueName) =>
        Execute<bool>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<bool>.Success(false);

            return OperationResult<bool>.Success(key.GetValue(valueName) is not null);
        }, keyPath);

    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) =>
        Execute<IReadOnlyList<string>>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<IReadOnlyList<string>>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            return OperationResult<IReadOnlyList<string>>.Success(key.GetSubKeyNames());
        }, keyPath);

    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) =>
        Execute<IReadOnlyList<string>>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<IReadOnlyList<string>>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            return OperationResult<IReadOnlyList<string>>.Success(key.GetValueNames());
        }, keyPath);

    public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) =>
        Execute<string>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<string>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            var value = key.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
                return OperationResult<string>.Failure($"Value not found: {keyPath}\\{valueName}", ErrorCategory.NotFound);

            return OperationResult<string>.Success(value switch
            {
                string[] arr => string.Join('\0', arr),
                _ => value.ToString() ?? string.Empty
            });
        }, keyPath);

    public OperationResult<RegistryValueData> ReadValue(string keyPath, string valueName) =>
        Execute<RegistryValueData>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<RegistryValueData>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            var raw = key.GetValue(valueName, defaultValue: null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is null)
                return OperationResult<RegistryValueData>.Failure($"Value not found: {keyPath}\\{valueName}", ErrorCategory.NotFound);

            var data = key.GetValueKind(valueName) switch
            {
                RegistryValueKind.DWord => RegistryValueData.FromDWord((int)raw),
                RegistryValueKind.QWord => RegistryValueData.FromQWord((long)raw),
                RegistryValueKind.MultiString => RegistryValueData.FromMultiString((string[])raw),
                RegistryValueKind.ExpandString => RegistryValueData.FromExpandString((string)raw),
                RegistryValueKind.String => RegistryValueData.FromString((string)raw),
                // Binary, None, and unknown kinds all read as bytes.
                _ => RegistryValueData.FromBinary(raw as byte[] ?? []),
            };
            return OperationResult<RegistryValueData>.Success(data);
        }, keyPath);

    public OperationResult<bool> WriteValue(string keyPath, string valueName, RegistryValueData value) =>
        Execute<bool>(() =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Kind switch
            {
                RegistryValueDataKind.DWord => WriteValueCore(keyPath, valueName, value.AsDWord(), RegistryValueKind.DWord),
                RegistryValueDataKind.QWord => WriteValueCore(keyPath, valueName, value.AsQWord(), RegistryValueKind.QWord),
                RegistryValueDataKind.MultiString => WriteValueCore(keyPath, valueName, value.AsMultiString(), RegistryValueKind.MultiString),
                RegistryValueDataKind.ExpandString => WriteValueCore(keyPath, valueName, value.Data, RegistryValueKind.ExpandString),
                RegistryValueDataKind.Binary => WriteValueCore(keyPath, valueName, value.AsBinary(), RegistryValueKind.Binary),
                _ => WriteValueCore(keyPath, valueName, value.Data, RegistryValueKind.String),
            };
        }, keyPath);

    /// <summary>RegQueryInfoKeyW's last-write FILETIME, as local time; null when the key is absent or unreadable.</summary>
    public DateTime? GetLastWriteTime(string keyPath)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return null;
            var status = RegQueryInfoKeyW(key.Handle.DangerousGetHandle(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, out var fileTime);
            return status == 0 ? DateTime.FromFileTime(fileTime) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.LibraryImport("advapi32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial int RegQueryInfoKeyW(nint hKey, nint lpClass, nint lpcchClass, nint lpReserved, nint lpcSubKeys,
        nint lpcbMaxSubKeyLen, nint lpcbMaxClassLen, nint lpcValues, nint lpcbMaxValueNameLen, nint lpcbMaxValueLen,
        nint lpcbSecurityDescriptor, out long lpftLastWriteTime);

    public OperationResult<bool> CreateKey(string keyPath) => Logged("CreateKey", keyPath, null,
        Execute<bool>(() =>
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.CreateSubKey(subKeyPath, writable: true);
            return OperationResult<bool>.Success(true);
        }, keyPath));

    private static OperationResult<T> ReadValueCore<T>(string keyPath, string valueName, Func<object, T> convert, bool doNotExpand = false)
    {
        var (root, subKeyPath) = ParseKeyPath(keyPath);
        using var key = root.OpenSubKey(subKeyPath, writable: false);
        if (key is null)
            return OperationResult<T>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

        var options = doNotExpand ? RegistryValueOptions.DoNotExpandEnvironmentNames : RegistryValueOptions.None;
        var raw = key.GetValue(valueName, defaultValue: null, options);
        if (raw is null)
            return OperationResult<T>.Failure($"Value not found: {keyPath}\\{valueName}", ErrorCategory.NotFound);

        return OperationResult<T>.Success(convert(raw));
    }

    private static OperationResult<bool> WriteValueCore(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        var (root, subKeyPath) = ParseKeyPath(keyPath);
        using var key = root.CreateSubKey(subKeyPath, writable: true);
        key.SetValue(valueName, value, kind);
        return OperationResult<bool>.Success(true);
    }

    private static OperationResult<T> Execute<T>(Func<OperationResult<T>> operation, string context)
    {
        try
        {
            return operation();
        }
        catch (ArgumentException ex)
        {
            return OperationResult<T>.Failure(
                $"Invalid registry path: {context}: {ex.Message}", ErrorCategory.NotFound, ex);
        }
        catch (SecurityException ex)
        {
            return OperationResult<T>.Failure(
                $"Access denied: {context}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<T>.Failure(
                $"Access denied: {context}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<T>.Failure(
                $"Registry operation failed on {context}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static (RegistryKey Root, string SubKeyPath) ParseKeyPath(string keyPath)
    {
        var separatorIndex = keyPath.IndexOf('\\', StringComparison.Ordinal);
        if (separatorIndex < 0)
            throw new ArgumentException($"Invalid registry key path (no subkey separator): {keyPath}", nameof(keyPath));

        var rootName = keyPath[..separatorIndex].ToUpperInvariant();
        var subKeyPath = keyPath[(separatorIndex + 1)..];

        var root = rootName switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Microsoft.Win32.Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Microsoft.Win32.Registry.LocalMachine,
            "HKCR" or "HKEY_CLASSES_ROOT" => Microsoft.Win32.Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Microsoft.Win32.Registry.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => Microsoft.Win32.Registry.CurrentConfig,
            _ => throw new ArgumentException($"Unknown registry root hive: {rootName}", nameof(keyPath))
        };

        return (root, subKeyPath);
    }
}
