using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Modules.Shell.Services;

/// <summary>
/// Reads the live value of every catalogued ExplorerPatcher setting, and
/// decides which ones apply on this machine.
///
/// Nothing here calls into ExplorerPatcher. It watches its own keys with
/// RegNotifyChangeKeyValue (its SettingsMonitor.c), so writing the value is
/// the whole interface; the settings marked as needing a restart are the ones
/// its hooks read once when Explorer starts.
/// </summary>
public sealed class ExplorerPatcherSettingsReader
{
    /// <summary>ExplorerPatcher's own key.</summary>
    public const string ExplorerPatcherKeyPath = @"HKCU\Software\ExplorerPatcher";

    /// <summary>Written by its installer ({CLSID}_ExplorerPatcher); present means installed.</summary>
    public const string UninstallKeyPath =
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{D17F1E1A-5919-4427-8F89-A1A8503CA3EB}_ExplorerPatcher";

    private const string AltTabSettingsValueName = "AltTabSettings";
    private const string PeopleBandKeyPath = ShellRegistryPaths.AdvancedKeyPath + @"\People";
    private const string StartClassicModeValueName = "Start_ShowClassicMode";
    private const int Windows11Version22H2Build = 22621;

    private readonly IRegistryService _registryService;

    public ExplorerPatcherSettingsReader(IRegistryService registryService)
    {
        _registryService = registryService;
    }

    /// <summary>True when ExplorerPatcher is installed, so its settings mean something.</summary>
    public bool IsInstalled() =>
        _registryService.KeyExists(UninstallKeyPath) is { IsSuccess: true, Value: true };

    /// <summary>
    /// Every catalogued setting with its live value and availability. Values
    /// that are absent read as null, which means ExplorerPatcher falls back to
    /// the default the catalog carries.
    /// </summary>
    public IReadOnlyList<ExplorerPatcherSetting> ReadAll()
    {
        var taskbarStyle = ReadTaskbarStyle();
        var settings = new List<ExplorerPatcherSetting>(ExplorerPatcherCatalog.Entries.Count);

        foreach (var setting in ExplorerPatcherCatalog.Entries)
        {
            var read = _registryService.ReadDWord(setting.RegistryKeyPath, setting.RegistryValueName);
            settings.Add(setting with
            {
                CurrentValue = read.IsSuccess ? read.Value : null,
                IsAvailable = Applies(setting.Condition, taskbarStyle),
            });
        }

        return settings.AsReadOnly();
    }

    /// <summary>
    /// ExplorerPatcher's taskbar style: 0 Windows 11, 1 Windows 10, 2 its own
    /// implementation. It defaults to its own on Windows 11. Its GUI further
    /// adjusts the value down when the files behind a style are missing; a
    /// machine in that state has a broken install, so the raw value is used.
    /// </summary>
    private int ReadTaskbarStyle()
    {
        var read = _registryService.ReadDWord(ExplorerPatcherKeyPath, "OldTaskbar");
        if (read.IsSuccess)
            return read.Value;
        return Environment.OSVersion.Version.Build >= 22000 ? 2 : 1;
    }

    /// <summary>
    /// Evaluates a section condition exactly as ExplorerPatcher's settings
    /// reader does (GUI.c). An unknown condition counts as true: the row then
    /// shows and writes a real value, which beats hiding a setting the person
    /// went looking for.
    /// </summary>
    private bool Applies(string condition, int taskbarStyle)
    {
        switch (condition)
        {
            case "":
                return true;
            case "IsOldTaskbar":
                return taskbarStyle != 0;
            case "!IsOldTaskbar":
                return taskbarStyle == 0;
            case "IsStockWin10Taskbar":
                return taskbarStyle == 1;
            case "IsAltImplTaskbar":
                return taskbarStyle > 1;
            case "IsWindows11Version22H2OrHigher":
                return Environment.OSVersion.Version.Build >= Windows11Version22H2Build;
            case "!IsWindows11Version22H2OrHigher":
                return Environment.OSVersion.Version.Build < Windows11Version22H2Build;
            case "!(IsWindows11Version22H2OrHigher&&!IsOldTaskbar)":
                return !(Environment.OSVersion.Version.Build >= Windows11Version22H2Build && taskbarStyle == 0);
            case "IsSWSEnabled":
                return ReadDWordOrDefault(ShellRegistryPaths.ExplorerKeyPath, AltTabSettingsValueName, 0) == 2;
            case "IsWeatherEnabled":
                return ReadDWordOrDefault(PeopleBandKeyPath, "PeopleBand", 0) == 1;
            case "DoesWindows10StartMenuExist":
                return Windows10StartMenuExists();
            case "IsWindows10StartMenu":
                return Windows10StartMenuExists()
                    && ReadDWordOrDefault(ShellRegistryPaths.AdvancedKeyPath, StartClassicModeValueName, 0) == 1;
            case "!IsWindows10StartMenu":
                return !(Windows10StartMenuExists()
                    && ReadDWordOrDefault(ShellRegistryPaths.AdvancedKeyPath, StartClassicModeValueName, 0) == 1);
            case "LogonLogoffShutdownSoundsAvailable":
                // ExplorerPatcher hides this one unconditionally in its own UI.
                return false;
            default:
                return true;
        }
    }

    private int ReadDWordOrDefault(string keyPath, string valueName, int fallback)
    {
        var read = _registryService.ReadDWord(keyPath, valueName);
        return read.IsSuccess ? read.Value : fallback;
    }

    /// <summary>The Windows 10 Start menu is only available while its host DLL is still shipped.</summary>
    private static bool Windows10StartMenuExists()
    {
        if (Environment.OSVersion.Version.Build < 22000)
            return true;
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SystemApps",
            "Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy",
            "StartUI.dll");
        return File.Exists(path);
    }
}
