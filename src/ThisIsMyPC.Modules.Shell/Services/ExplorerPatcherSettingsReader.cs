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
    private const int Windows11Build = 22000;
    private const string OldTaskbarValueName = "OldTaskbar";
    private const string ReplaceVanValueName = "ReplaceVan";

    // Windows 10 taskbar styles as ExplorerPatcher numbers them.
    private const int Windows11Taskbar = 0;
    private const int StockWindows10Taskbar = 1;
    private const int ExplorerPatcherTaskbar = 2;

    /// <summary>Microsoft removed the stock Windows 10 taskbar from explorer.exe here (x64; utility.h IsStockWindows10TaskbarAvailable).</summary>
    private const int StockWindows10TaskbarRemovedBuild = 26002;

    /// <summary>van.dll, the Windows 8 network flyout, left here (GUI.c, ReplaceVan).</summary>
    private const int Windows8NetworkFlyoutRemovedBuild = 25346;

    private readonly IRegistryService _registryService;
    private readonly int _buildNumber;
    private readonly Func<string, bool> _fileExists;

    /// <param name="buildNumber">Windows build to evaluate against; this machine's when null. Tests pin it.</param>
    /// <param name="fileExists">Probe for ExplorerPatcher's own files; the file system when null.</param>
    public ExplorerPatcherSettingsReader(
        IRegistryService registryService,
        int? buildNumber = null,
        Func<string, bool>? fileExists = null)
    {
        _registryService = registryService;
        _buildNumber = buildNumber ?? Environment.OSVersion.Version.Build;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>
    /// Where ExplorerPatcher's own taskbar lives for this build, or null when
    /// it ships none (utility.h PickTaskbarDll). The DLL is named after the
    /// codename of the first release each variant supports.
    /// </summary>
    public string? TaskbarDllPath()
    {
        var b = _buildNumber;
        var name = b switch
        {
            15063 or 16299 or 17134 or 17763 => "ep_taskbar.rs2.dll",
            >= 18362 and <= 18363 => "ep_taskbar.rs2.dll",
            >= 19041 and <= 19045 => "ep_taskbar.rs2.dll",
            20348 => "ep_taskbar.fe.dll",
            >= 21343 and <= 22000 => "ep_taskbar.co.dll",
            >= 22621 and <= 22635 => "ep_taskbar.ni.dll",
            >= 23403 and <= 25197 => "ep_taskbar.ni.dll",
            >= 25201 and <= 25915 => "ep_taskbar.zn.dll",
            >= 25921 => "ep_taskbar.ge.dll",
            _ => null,
        };
        return name is null
            ? null
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ExplorerPatcher", name);
    }

    private bool TaskbarDllExists() => TaskbarDllPath() is { } path && _fileExists(path);

    private bool StockWindows10TaskbarAvailable() => _buildNumber < StockWindows10TaskbarRemovedBuild;

    /// <summary>
    /// What ExplorerPatcher makes of a taskbar style value on this machine
    /// (utility.h AdjustTaskbarStyleValue): its own taskbar needs its DLL for
    /// this build, the stock Windows 10 taskbar needs a build that still has
    /// one, and Windows 10 has no Windows 11 taskbar to fall back to.
    /// </summary>
    public int AdjustTaskbarStyle(int value)
    {
        if (value >= ExplorerPatcherTaskbar && !TaskbarDllExists())
            value = StockWindows10Taskbar;
        if (_buildNumber >= Windows11Build)
        {
            if (value == StockWindows10Taskbar && !StockWindows10TaskbarAvailable())
                value = Windows11Taskbar;
        }
        else if (value == Windows11Taskbar)
        {
            value = StockWindows10Taskbar;
        }
        return value;
    }

    /// <summary>
    /// The options ExplorerPatcher's own window offers on this machine (GUI.c
    /// GUI_RemoveChoiceEntry): a taskbar style whose files are gone and a
    /// network flyout Windows no longer ships are dropped, since choosing
    /// them does nothing.
    /// </summary>
    private IReadOnlyList<ExplorerPatcherOption> OptionsFor(ExplorerPatcherSetting setting)
    {
        switch (setting.RegistryValueName)
        {
            case OldTaskbarValueName:
                return setting.Options
                    .Where(o => o.Value != StockWindows10Taskbar || _buildNumber < Windows11Build || StockWindows10TaskbarAvailable())
                    .Where(o => o.Value != ExplorerPatcherTaskbar || TaskbarDllExists())
                    .ToList()
                    .AsReadOnly();
            case ReplaceVanValueName:
                return setting.Options
                    .Where(o => o.Value != 2 || _buildNumber < Windows8NetworkFlyoutRemovedBuild)
                    .ToList()
                    .AsReadOnly();
            default:
                return setting.Options;
        }
    }

    /// <summary>True when ExplorerPatcher is installed, so its settings mean something.</summary>
    public bool IsInstalled() =>
        _registryService.KeyExists(UninstallKeyPath) is { IsSuccess: true, Value: true };

    /// <summary>
    /// The installed version, as its own setup records it. Empty when it is
    /// not installed or the value is missing. The app compares this with the
    /// version the catalog was pinned to.
    /// </summary>
    public string InstalledVersion() =>
        _registryService.ReadString(UninstallKeyPath, "DisplayVersion") is { IsSuccess: true, Value: { } version }
            ? version
            : string.Empty;

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
            int? live = read.IsSuccess ? read.Value : null;
            int? adjusted = null;
            if (setting.RegistryValueName == OldTaskbarValueName)
            {
                var inForce = AdjustTaskbarStyle(live ?? setting.DefaultValue);
                if (inForce != (live ?? setting.DefaultValue))
                    adjusted = inForce;
            }
            settings.Add(setting with
            {
                CurrentValue = live,
                AdjustedValue = adjusted,
                Options = OptionsFor(setting),
                IsAvailable = Applies(setting.Condition, taskbarStyle),
            });
        }

        return settings.AsReadOnly();
    }

    /// <summary>
    /// ExplorerPatcher's taskbar style as it is in force: 0 Windows 11, 1 the
    /// stock Windows 10 one, 2 its own implementation. It defaults to its own
    /// on Windows 11, and a value whose files are missing on this build reads
    /// as the next one down, exactly as its GUI.c evaluates its conditions.
    /// </summary>
    private int ReadTaskbarStyle()
    {
        var read = _registryService.ReadDWord(ExplorerPatcherKeyPath, OldTaskbarValueName);
        var raw = read.IsSuccess
            ? read.Value
            : (_buildNumber >= Windows11Build ? ExplorerPatcherTaskbar : StockWindows10Taskbar);
        return AdjustTaskbarStyle(raw);
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
                return _buildNumber >= Windows11Version22H2Build;
            case "!IsWindows11Version22H2OrHigher":
                return _buildNumber < Windows11Version22H2Build;
            case "!(IsWindows11Version22H2OrHigher&&!IsOldTaskbar)":
                return !(_buildNumber >= Windows11Version22H2Build && taskbarStyle == 0);
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
