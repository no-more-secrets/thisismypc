namespace ThisIsMyPC.Modules.Shell;

/// <summary>
/// Shared registry path constants used across Shell module scanners, change factories, and the module itself.
/// </summary>
public static class ShellRegistryPaths
{
    public const string AdvancedKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string ExplorerKeyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";
    public const string ClassicContextMenuClsidKeyPath = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    public const string ClassicContextMenuKeyPath = ClassicContextMenuClsidKeyPath + @"\InprocServer32";
    public const string AbsentValue = "__absent__";

    /// <summary>
    /// Splits a SystemLocation (e.g., "HKCR\...\ValueName") into key path and value name.
    /// Maps "(Default)" to empty string per Windows registry API convention.
    /// </summary>
    public static (string KeyPath, string ValueName) ParseSystemLocation(string systemLocation)
    {
        var lastSep = systemLocation.LastIndexOf('\\');
        if (lastSep < 0)
            throw new ArgumentException($"Invalid system location (no separator): {systemLocation}");

        var valueName = systemLocation[(lastSep + 1)..];

        if (valueName == "(Default)")
            valueName = string.Empty;

        return (systemLocation[..lastSep], valueName);
    }
}
