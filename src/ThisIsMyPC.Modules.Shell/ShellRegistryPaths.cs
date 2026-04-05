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
    public const string CommandBarClsidKeyPath = @"HKCU\Software\Classes\CLSID\{d93ed569-3b3e-4bff-8355-3c44f6a52bb5}";
    public const string CommandBarKeyPath = CommandBarClsidKeyPath + @"\InprocServer32";
    public const string BlockedListKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    public const string AbsentValue = "__absent__";

    // Static verb scope paths — the 10 hierarchy levels from which Shell enumerates \shell verbs
    public static IReadOnlyList<(string KeyPath, string Scope)> StaticVerbScopePaths { get; } =
    [
        (@"HKCR\*\shell", "All files"),
        (@"HKCR\AllFilesystemObjects\shell", "All filesystem objects"),
        (@"HKCR\Directory\shell", "Directories"),
        (@"HKCR\Folder\shell", "Folders"),
        (@"HKCR\Directory\Background\shell", "Folder background"),
        (@"HKCR\DesktopBackground\shell", "Desktop background"),
        (@"HKCR\Drive\shell", "Drives"),
    ];

    /// <summary>
    /// Remaps an HKCR path to its HKCU\Software\Classes equivalent.
    /// HKCR is a virtual merge of HKLM\SOFTWARE\Classes + HKCU\Software\Classes,
    /// with HKCU taking precedence. Writing to HKCU avoids TrustedInstaller ownership
    /// on system keys while achieving the same effect.
    /// </summary>
    public static string RemapHkcrToHkcu(string hkcrPath)
    {
        if (hkcrPath.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase))
            return @"HKCU\Software\Classes\" + hkcrPath[5..];
        return hkcrPath;
    }

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
