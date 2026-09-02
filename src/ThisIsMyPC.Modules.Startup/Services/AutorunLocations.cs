using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>How a registry location's items are read.</summary>
/// <param name="Category">Autoruns tab the location belongs to.</param>
/// <param name="KeyPath">The key whose values or subkeys are the items.</param>
/// <param name="Kind">Values (RegistryValue) or subkeys (RegistryKey).</param>
/// <param name="DataValueName">For subkey items: the value inside the subkey that names the file or CLSID; null means the subkey's default value. Ignored for value items (their own data).</param>
/// <param name="DescriptionValueName">For subkey items: the value inside the subkey that describes it; null means the subkey's default value.</param>
/// <param name="OnlyValues">For value items: list only these value names (SafeBoot's AlternateShell).</param>
/// <param name="SkipValues">For value items: value names that are not items (KnownDlls' DllDirectory).</param>
/// <param name="RequireData">For subkey items: skip subkeys that lack the data value (Active Setup components without a StubPath never run).</param>
/// <param name="Is32Bit">Items under a WOW6432Node key: bare DLL names resolve against SysWOW64.</param>
public sealed record AutorunLocation(
    AutorunCategory Category,
    string KeyPath,
    AutorunItemKind Kind,
    string? DataValueName = null,
    string? DescriptionValueName = null,
    IReadOnlyList<string>? OnlyValues = null,
    IReadOnlyList<string>? SkipValues = null,
    bool RequireData = false,
    bool Is32Bit = false);

/// <summary>
/// The registry locations Autoruns reads, by tab. Startup folders, scheduled
/// tasks, services, and drivers are scanned by their own code paths.
/// </summary>
public static class AutorunLocations
{
    public const string ActiveSetupKey = @"HKLM\SOFTWARE\Microsoft\Active Setup\Installed Components";
    public const string SafeBootKey = @"HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot";
    public const string ServicesKey = @"HKLM\SYSTEM\CurrentControlSet\Services";
    public const string BackgroundContextMenuHandlersKey = @"HKLM\SOFTWARE\Classes\Directory\Background\ShellEx\ContextMenuHandlers";
    public const string ShellIconOverlayKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers";

    /// <summary>Windows' own off switch for shell extensions (CLSID value names); the Context Menus page writes it.</summary>
    public const string BlockedShellExtensionsKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    public const string BrowserHelperObjectsKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects";
    public const string BrowserHelperObjects32Key = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects";
    public const string FontDriversKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Font Drivers";
    public const string Drivers32Key = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Drivers32";
    public const string Drivers32WowKey = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Drivers32";
    public const string KnownDllsKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDlls";
    public const string CredentialProvidersKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers";
    public const string WinsockCatalogKey = @"HKLM\SYSTEM\CurrentControlSet\Services\WinSock2\Parameters\NameSpace_Catalog5\Catalog_Entries";
    public const string WinsockCatalog64Key = @"HKLM\SYSTEM\CurrentControlSet\Services\WinSock2\Parameters\NameSpace_Catalog5\Catalog_Entries64";
    public const string PrintMonitorsKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Print\Monitors";
    public const string OfficeKey = @"HKLM\SOFTWARE\Microsoft\Office";
    public const string Office32Key = @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Office";

    public static IReadOnlyList<AutorunLocation> Registry { get; } = Build();

    private static AutorunLocation[] Build()
    {
        var list = new List<AutorunLocation>
        {
            // Logon
            new(AutorunCategory.Logon, StartupScanner.UserRunKey, AutorunItemKind.RegistryValue),
            new(AutorunCategory.Logon, StartupScanner.MachineRunKey, AutorunItemKind.RegistryValue),
            new(AutorunCategory.Logon, StartupScanner.MachineRunWow64Key, AutorunItemKind.RegistryValue, Is32Bit: true),
            new(AutorunCategory.Logon, ActiveSetupKey, AutorunItemKind.RegistryKey, DataValueName: "StubPath", RequireData: true),
            new(AutorunCategory.Logon, SafeBootKey, AutorunItemKind.RegistryValue, OnlyValues: ["AlternateShell"]),

            // Explorer
            new(AutorunCategory.Explorer, BackgroundContextMenuHandlersKey, AutorunItemKind.RegistryKey),
            new(AutorunCategory.Explorer, ShellIconOverlayKey, AutorunItemKind.RegistryKey),

            // Internet Explorer
            new(AutorunCategory.InternetExplorer, BrowserHelperObjectsKey, AutorunItemKind.RegistryKey),
            new(AutorunCategory.InternetExplorer, BrowserHelperObjects32Key, AutorunItemKind.RegistryKey, Is32Bit: true),

            // Font drivers, 32-bit drivers, known DLLs: values whose data is a DLL
            new(AutorunCategory.FontDrivers, FontDriversKey, AutorunItemKind.RegistryValue),
            new(AutorunCategory.Drivers32, Drivers32Key, AutorunItemKind.RegistryValue),
            new(AutorunCategory.Drivers32, Drivers32WowKey, AutorunItemKind.RegistryValue, Is32Bit: true),
            new(AutorunCategory.KnownDlls, KnownDllsKey, AutorunItemKind.RegistryValue, SkipValues: ["DllDirectory", "DllDirectory32"]),

            // Winlogon
            new(AutorunCategory.Winlogon, CredentialProvidersKey, AutorunItemKind.RegistryKey),

            // Winsock
            new(AutorunCategory.WinsockProviders, WinsockCatalogKey, AutorunItemKind.RegistryKey, DataValueName: "LibraryPath", DescriptionValueName: "DisplayString"),
            new(AutorunCategory.WinsockProviders, WinsockCatalog64Key, AutorunItemKind.RegistryKey, DataValueName: "LibraryPath", DescriptionValueName: "DisplayString"),

            // Print monitors
            new(AutorunCategory.PrintMonitors, PrintMonitorsKey, AutorunItemKind.RegistryKey, DataValueName: "Driver"),
        };

        // Office add-ins: the subkey name is the add-in's ProgID.
        foreach (var app in new[] { "Outlook", "Excel", "PowerPoint", "Word" })
        {
            list.Add(new(AutorunCategory.Office, $@"{OfficeKey}\{app}\Addins", AutorunItemKind.RegistryKey,
                DataValueName: null, DescriptionValueName: "FriendlyName"));
        }
        foreach (var app in new[] { "Outlook", "Excel", "PowerPoint", "Word" })
        {
            list.Add(new(AutorunCategory.Office, $@"{Office32Key}\{app}\Addins", AutorunItemKind.RegistryKey,
                DataValueName: null, DescriptionValueName: "FriendlyName", Is32Bit: true));
        }

        return [.. list];
    }
}
