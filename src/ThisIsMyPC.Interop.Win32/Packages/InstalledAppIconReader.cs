using Microsoft.Win32;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Shell;

namespace ThisIsMyPC.Interop.Win32.Packages;

/// <summary>Reads registered desktop app artwork for the unelevated UI.</summary>
public static class InstalledAppIconReader
{
    private static readonly Lazy<IReadOnlyDictionary<string, (string Path, int Index)>> Icons = new(ReadRegistry);

    public static FileIcon? Read(string displayName)
    {
        if (!Icons.Value.TryGetValue(displayName, out var path)) return null;
        var result = new FileIconService().GetApplicationIcon(path.Path, path.Index);
        return result.IsSuccess ? result.Value : null;
    }

    private static IReadOnlyDictionary<string, (string Path, int Index)> ReadRegistry()
    {
        var icons = new Dictionary<string, (string Path, int Index)>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var app = uninstall.OpenSubKey(name);
                        if (app?.GetValue("DisplayName") is not string displayName || app.GetValue("DisplayIcon") is not string icon) continue;
                        var path = ParseIconLocation(icon);
                        if (path is { } location && File.Exists(location.Path)) icons.TryAdd(displayName, location);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return icons;
    }

    public static (string Path, int Index)? ParseIconLocation(string value)
    {
        value = Environment.ExpandEnvironmentVariables(value.Trim());
        var index = 0;
        var comma = value.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(value[(comma + 1)..], out var parsed))
        {
            index = parsed;
            value = value[..comma].Trim();
        }
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end < 0) return null;
            value = value[1..end];
        }
        return Path.IsPathFullyQualified(value) && !value.StartsWith(@"\\", StringComparison.Ordinal) ? (value, index) : null;
    }
}
