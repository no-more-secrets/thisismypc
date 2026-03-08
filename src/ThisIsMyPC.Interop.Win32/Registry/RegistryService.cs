using System.Security;
using Microsoft.Win32;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Registry;

public sealed class RegistryService : IRegistryService
{
    public OperationResult<int> ReadDWord(string keyPath, string valueName)
    {
        return ReadValue(keyPath, valueName, raw =>
        {
            if (raw is int intVal)
                return intVal;
            return Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
        });
    }

    public OperationResult<string> ReadString(string keyPath, string valueName)
    {
        return ReadValue(keyPath, valueName, raw => raw?.ToString() ?? string.Empty);
    }

    public OperationResult<string> ReadExpandString(string keyPath, string valueName)
    {
        return ReadValue(keyPath, valueName, raw => raw?.ToString() ?? string.Empty, doNotExpand: true);
    }

    public OperationResult<string[]> ReadMultiString(string keyPath, string valueName)
    {
        return ReadValue(keyPath, valueName, raw =>
        {
            if (raw is string[] arr)
                return arr;
            return [raw?.ToString() ?? string.Empty];
        });
    }

    public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value)
    {
        return WriteValue(keyPath, valueName, value, RegistryValueKind.DWord);
    }

    public OperationResult<bool> WriteString(string keyPath, string valueName, string value)
    {
        return WriteValue(keyPath, valueName, value, RegistryValueKind.String);
    }

    public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value)
    {
        return WriteValue(keyPath, valueName, value, RegistryValueKind.ExpandString);
    }

    public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values)
    {
        return WriteValue(keyPath, valueName, values, RegistryValueKind.MultiString);
    }

    public OperationResult<bool> DeleteValue(string keyPath, string valueName)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: true);
            if (key is null)
                return OperationResult<bool>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            key.DeleteValue(valueName, throwOnMissingValue: false);
            return OperationResult<bool>.Success(true);
        }
        catch (SecurityException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure($"Failed to delete value '{valueName}' in {keyPath}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            if (recursive)
                root.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
            else
                root.DeleteSubKey(subKeyPath, throwOnMissingSubKey: false);

            return OperationResult<bool>.Success(true);
        }
        catch (SecurityException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure($"Failed to delete key {keyPath}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> KeyExists(string keyPath)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            return OperationResult<bool>.Success(key is not null);
        }
        catch (SecurityException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure($"Failed to check key existence: {keyPath}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<bool> ValueExists(string keyPath, string valueName)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<bool>.Success(false);

            return OperationResult<bool>.Success(key.GetValue(valueName) is not null);
        }
        catch (SecurityException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure($"Failed to check value existence: {keyPath}\\{valueName}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<IReadOnlyList<string>>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            return OperationResult<IReadOnlyList<string>>.Success(key.GetSubKeyNames());
        }
        catch (SecurityException ex)
        {
            return OperationResult<IReadOnlyList<string>>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<IReadOnlyList<string>>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<string>>.Failure($"Failed to enumerate subkeys: {keyPath}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key is null)
                return OperationResult<IReadOnlyList<string>>.Failure($"Key not found: {keyPath}", ErrorCategory.NotFound);

            return OperationResult<IReadOnlyList<string>>.Success(key.GetValueNames());
        }
        catch (SecurityException ex)
        {
            return OperationResult<IReadOnlyList<string>>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<IReadOnlyList<string>>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<string>>.Failure($"Failed to enumerate values: {keyPath}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName)
    {
        try
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
        }
        catch (SecurityException ex)
        {
            return OperationResult<string>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<string>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Failure($"Failed to read value: {keyPath}\\{valueName}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static OperationResult<T> ReadValue<T>(string keyPath, string valueName, Func<object, T> convert, bool doNotExpand = false)
    {
        try
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
        catch (SecurityException ex)
        {
            return OperationResult<T>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<T>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<T>.Failure($"Failed to read {keyPath}\\{valueName}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static OperationResult<bool> WriteValue(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        try
        {
            var (root, subKeyPath) = ParseKeyPath(keyPath);
            using var key = root.CreateSubKey(subKeyPath, writable: true);
            key.SetValue(valueName, value, kind);
            return OperationResult<bool>.Success(true);
        }
        catch (SecurityException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<bool>.Failure($"Access denied: {keyPath}", ErrorCategory.AccessDenied, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure($"Failed to write {keyPath}\\{valueName}: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
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
